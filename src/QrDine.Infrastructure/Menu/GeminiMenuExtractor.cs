using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QrDine.Application.Menu;
using QrDine.Domain.Common;

namespace QrDine.Infrastructure.Menu;

/// <summary>
/// Extracts a menu from a photo using Google's Gemini API (free tier). Configure
/// with Gemini:ApiKey (env Gemini__ApiKey) + optional Gemini:Model. No key ⇒ a
/// friendly error so the CSV import still works without any setup.
///
/// Notes for reliability: we ask for JSON via responseMimeType and describe the exact
/// shape in the prompt instead of sending a rigid responseSchema (the schema form is
/// easy to get subtly wrong and Gemini then 400s the whole request). Parsing is
/// tolerant of markdown code fences and of an array-or-object top level. The real
/// upstream error is logged so an operator can see exactly why a photo failed.
/// </summary>
public class GeminiMenuExtractor : IMenuVisionExtractor
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiMenuExtractor> _log;

    public GeminiMenuExtractor(HttpClient http, IConfiguration config, ILogger<GeminiMenuExtractor> log)
    {
        _http = http;
        _log = log;
        _apiKey = (config["Gemini:ApiKey"] ?? "").Trim();
        // Google retires dated models often. This name follows whichever Flash model is current.
        _model = (config["Gemini:Model"] ?? "gemini-flash-latest").Trim();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

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

        // The key goes in a header, so it can't end up in a log with the address.
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", _apiKey);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(request, ct); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gemini menu extract: network error calling model {Model}", _model);
            throw new DomainException("We couldn't reach the service that reads photos. Try again in a moment, or use the spreadsheet import.");
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Gemini menu extract failed: {Status} model={Model} body={Body}",
                (int)resp.StatusCode, _model, Truncate(body, 800));
            // Gemini reports a bad key as a 400 (INVALID_ARGUMENT) — surface the key hint.
            var keyProblem = body.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
                || body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
                || body.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase);
            var modelProblem = body.Contains("is not found", StringComparison.OrdinalIgnoreCase)
                || body.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase);
            throw new DomainException(
                keyProblem || modelProblem ? "Reading photos isn't working right now. Please tell Dine In, and use the spreadsheet import for now."
                : FriendlyFor((int)resp.StatusCode));
        }

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
        401 or 403 => "Reading photos isn't working right now. Please tell Dine In, and use the spreadsheet import for now.",
        429 => "Too many photos are being read right now. Wait a minute and try again, or use the spreadsheet import.",
        >= 500 => "The service that reads photos is having a problem. Try again shortly, or use the spreadsheet import.",
        _ => "The photo couldn't be read right now. Try a clearer photo, or use the spreadsheet import.",
    };

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
