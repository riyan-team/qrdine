using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QrDine.Application.Menu;
using QrDine.Domain.Common;

namespace QrDine.Infrastructure.Menu;

/// <summary>
/// Extracts a menu from a photo using Google's Gemini API (free tier). Configure
/// with Gemini:ApiKey (env Gemini__ApiKey) + optional Gemini:Model. No key ⇒ a
/// friendly error so the CSV import still works without any setup.
/// </summary>
public class GeminiMenuExtractor : IMenuVisionExtractor
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;

    public GeminiMenuExtractor(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Gemini:ApiKey"];
        _model = config["Gemini:Model"] ?? "gemini-2.0-flash";
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<MenuImportPreview> Extract(byte[] image, string mimeType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new DomainException("Photo import isn't set up yet — add a Gemini API key, or use the spreadsheet import.");

        const string prompt =
            "You are reading a restaurant menu image. Extract every category with its items and prices. " +
            "Group items under their section/category headings exactly as printed (e.g. Biryani, Karahi, Drinks). " +
            "Price must be a plain number in the menu's currency (strip any symbols, commas, or size labels). " +
            "If an item has multiple sizes, use the largest/full price. " +
            "If an item shows a discount or deal (e.g. a crossed-out original price, or 'was 500 now 400'), " +
            "set price to the ORIGINAL price and dealPrice to the DISCOUNTED price; otherwise omit dealPrice. " +
            "Ignore descriptions, phone numbers and addresses. Return JSON only.";

        var payload = new
        {
            contents = new[]
            {
                new { parts = new object[]
                {
                    new { text = prompt },
                    new { inline_data = new { mime_type = mimeType, data = Convert.ToBase64String(image) } },
                }}
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        categories = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    items = new
                                    {
                                        type = "array",
                                        items = new
                                        {
                                            type = "object",
                                            properties = new
                                            {
                                                name = new { type = "string" },
                                                price = new { type = "number" },
                                                dealPrice = new { type = "number", nullable = true },
                                            },
                                            required = new[] { "name", "price" },
                                        },
                                    },
                                },
                                required = new[] { "name", "items" },
                            },
                        },
                    },
                    required = new[] { "categories" },
                },
            },
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        HttpResponseMessage resp;
        try { resp = await _http.PostAsync(url, content, ct); }
        catch (Exception) { throw new DomainException("Couldn't reach the photo-reading service. Try again or use the spreadsheet import."); }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new DomainException("The photo couldn't be read (service error). Try a clearer photo or the spreadsheet import.");

        // candidates[0].content.parts[0].text holds the JSON (responseMimeType=application/json).
        try
        {
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text").GetString() ?? "";
            var preview = JsonSerializer.Deserialize<MenuImportPreview>(text, JsonOpts);
            if (preview?.Categories is null || preview.Categories.Count == 0)
                throw new DomainException("No menu items were detected. Try a clearer, well-lit photo.");
            return preview;
        }
        catch (DomainException) { throw; }
        catch (Exception) { throw new DomainException("Couldn't understand the menu photo. Try a clearer photo or the spreadsheet import."); }
    }
}
