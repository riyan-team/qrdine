using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QrDine.Application.Menu;
using QrDine.Domain.Common;

namespace QrDine.Infrastructure.Menu;

/// <summary>
/// Extracts a menu from a photo using Google's Gemini API (free tier). Configure
/// with Gemini:ApiKey (env Gemini__ApiKey), optional Gemini:Model and optional
/// Gemini:FallbackModels (comma-separated). No key ⇒ a friendly error so the CSV
/// import still works without any setup.
///
/// Google's models are often briefly overloaded (503 "high demand") or rate-limited (429).
/// So a busy answer is retried once on the first model, then the next model is tried, all
/// within a time budget that stays under Cloudflare's 100-second limit. A refused key or a
/// photo Google can't use stops at once, because another model wouldn't change that.
///
/// Notes for reliability: we ask for JSON via responseMimeType and describe the exact
/// shape in the prompt instead of sending a rigid responseSchema (the schema form is
/// easy to get subtly wrong and Gemini then 400s the whole request). Parsing is
/// tolerant of markdown code fences and of an array-or-object top level. The real
/// upstream error is logged so an operator can see exactly why a photo failed.
/// </summary>
public class GeminiMenuExtractor : IMenuVisionExtractor
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PerTry = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ShortestTry = TimeSpan.FromSeconds(8);

    private const string NotWorking = "Reading photos isn't working right now. Please tell Dine In, and use the spreadsheet import for now.";
    private const string Busy = "Google's photo reading is very busy right now. Please try again in a few minutes, or use the spreadsheet import.";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly IReadOnlyList<string> _models;
    private readonly TimeSpan _retryDelay;
    private readonly ILogger<GeminiMenuExtractor> _log;

    public GeminiMenuExtractor(HttpClient http, IConfiguration config, ILogger<GeminiMenuExtractor> log)
    {
        _http = http;
        _log = log;
        _apiKey = (config["Gemini:ApiKey"] ?? "").Trim();
        // Google retires dated models often. These names follow whichever Flash models are current.
        var primary = (config["Gemini:Model"] ?? "").Trim();
        if (primary.Length == 0) primary = "gemini-flash-latest";
        var fallbacks = (config["Gemini:FallbackModels"] ?? "gemini-flash-lite-latest")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _models = new[] { primary }.Concat(fallbacks).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        _retryDelay = TimeSpan.FromMilliseconds(int.TryParse(config["Gemini:RetryDelayMs"], out var ms) ? Math.Max(0, ms) : 2000);
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private enum Outcome { Ok, Busy, NoSuchModel, KeyProblem, BadRequest }
    private sealed record Attempt(Outcome Outcome, int Status, string Body);

    public async Task<MenuImportPreview> Extract(byte[] image, string mimeType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new DomainException("Reading a menu from a photo isn't switched on yet. Please use the spreadsheet import for now.");

        const string prompt =
            "You are reading a photo of a restaurant menu. Extract EVERY category with its items and prices.\n" +
            "Group items under their printed section/category headings (e.g. Biryani, Karahi, Drinks, Deals).\n" +
            "name: the item or deal's SHORT title ONLY — at most 4 words (e.g. \"Chicken Biryani\", \"Family Deal 1\").\n" +
            "NEVER put the list of included items, a combo's contents, or any sentence/description in name.\n" +
            "price: a plain number in the menu's currency — strip symbols, commas and size labels; if an item lists\n" +
            "multiple sizes use the largest/full price.\n" +
            "dealPrice: if an item shows a discount/deal (a crossed-out original price, or 'was 500 now 400', or a\n" +
            "combo/deal price), set price to the ORIGINAL and dealPrice to the DISCOUNTED price; otherwise omit it.\n" +
            "dealDescription: for a deal/combo ONLY, put the full 'what's included' text here (e.g. \"2 Pizzas + \n" +
            "1.5L drink + garlic bread\"). Leave it out for normal items. This keeps the title short and the\n" +
            "combo contents in their own field.\n" +
            "Ignore plain item descriptions, phone numbers and addresses.\n" +
            "Return ONLY JSON in exactly this shape, no prose, no markdown:\n" +
            "{\"categories\":[{\"name\":\"string\",\"items\":[{\"name\":\"string\",\"price\":0,\"dealPrice\":0,\"dealDescription\":\"string\"}]}]}";

        var payload = new
        {
            contents = new[]
            {
                new { role = "user", parts = new object[]
                {
                    new { text = prompt },
                    new { inline_data = new { mime_type = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType, data = Convert.ToBase64String(image) } },
                }}
            },
            generationConfig = new { temperature = 0.1, responseMimeType = "application/json", maxOutputTokens = 32768 },
        };
        var json = JsonSerializer.Serialize(payload);

        var clock = Stopwatch.StartNew();
        var wasBusy = false;
        for (var i = 0; i < _models.Count; i++)
        {
            var model = _models[i];
            var tries = i == 0 ? 2 : 1;   // a busy first model gets one more go before moving on
            for (var t = 0; t < tries; t++)
            {
                if (t > 0) await Task.Delay(_retryDelay, ct);
                var left = Budget - clock.Elapsed;
                if (left < ShortestTry) throw new DomainException(wasBusy ? Busy : NotWorking);

                var attempt = await Call(model, json, left < PerTry ? left : PerTry, ct);
                switch (attempt.Outcome)
                {
                    case Outcome.Ok:
                        if (i > 0 || t > 0) _log.LogInformation("Gemini menu extract: read by {Model} after {Ms} ms", model, clock.ElapsedMilliseconds);
                        return Parse(attempt.Body);
                    case Outcome.KeyProblem:
                        throw new DomainException(NotWorking);
                    case Outcome.BadRequest:
                        throw new DomainException(FriendlyFor(attempt.Status));
                    case Outcome.Busy:
                        wasBusy = true;
                        continue;
                    case Outcome.NoSuchModel:
                        t = tries;   // no point asking this model again
                        break;
                }
            }
        }
        throw new DomainException(wasBusy ? Busy : NotWorking);
    }

    private async Task<Attempt> Call(string model, string json, TimeSpan limit, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        // The key goes in a header, so it can't end up in a log with the address.
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", _apiKey);
        try
        {
            using var resp = await _http.SendAsync(request, timeout.Token);
            var body = await resp.Content.ReadAsStringAsync(timeout.Token);
            var status = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode) return new Attempt(Outcome.Ok, status, body);
            _log.LogWarning("Gemini menu extract failed: {Status} model={Model} body={Body}", status, model, Truncate(body, 800));
            return new Attempt(Classify(status, body), status, body);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // A timeout or a dropped connection: treat like a busy model and try the next one.
            _log.LogWarning(ex, "Gemini menu extract: no answer from model {Model}", model);
            return new Attempt(Outcome.Busy, 0, "");
        }
    }

    private static Outcome Classify(int status, string body)
    {
        // Gemini reports a bad key as a 400 (INVALID_ARGUMENT), so look at the words too.
        if (status is 401 or 403
            || body.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
            || body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
            || body.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
            return Outcome.KeyProblem;
        if (status == 404 || body.Contains("is not found", StringComparison.OrdinalIgnoreCase)
            || body.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return Outcome.NoSuchModel;
        if (status is 408 or 429 || status >= 500) return Outcome.Busy;
        return Outcome.BadRequest;
    }

    private MenuImportPreview Parse(string body)
    {
        try
        {
            var text = ExtractText(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                _log.LogWarning("Gemini menu extract: empty response. body={Body}", Truncate(body, 800));
                throw new DomainException("No menu items were detected. Try a clearer, well-lit photo of the printed menu.");
            }
            var preview = JsonSerializer.Deserialize<MenuImportPreview>(StripFences(text), JsonOpts);
            var cats = preview?.Categories?.Where(c => c is not null && c.Items?.Count > 0).ToList();
            if (cats is null || cats.Count == 0)
                throw new DomainException("No menu items were detected. Try a clearer, well-lit photo of the printed menu.");
            return new MenuImportPreview(cats);
        }
        catch (DomainException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gemini menu extract: could not parse response. body={Body}", Truncate(body, 800));
            throw new DomainException("Couldn't read the menu from that photo. Try a clearer, straight-on photo, or use the spreadsheet import.");
        }
    }

    // candidates[0].content.parts[*].text — concatenated (responseMimeType=application/json).
    private static string ExtractText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
            return "";
        var first = cands[0];
        if (!first.TryGetProperty("content", out var contentEl) || !contentEl.TryGetProperty("parts", out var parts))
            return "";
        var sb = new StringBuilder();
        foreach (var p in parts.EnumerateArray())
            if (p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                sb.Append(t.GetString());
        return sb.ToString();
    }

    // Gemini sometimes wraps JSON in ```json ... ``` fences despite the mime type.
    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s.Trim();
    }

    private static string FriendlyFor(int status) => status switch
    {
        400 => "That photo couldn't be read. Please try a different photo, or use the spreadsheet import.",
        _ => "The photo couldn't be read right now. Try a clearer photo, or use the spreadsheet import.",
    };

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
