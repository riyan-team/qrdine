using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Menu;

namespace QrDine.Application.Menu;

// Shared shape for an imported/parsed menu the owner reviews before saving.
// DealDescription holds a combo's "what's included" text so it never bleeds into Name.
public record ImportItem(string Name, decimal Price, decimal? DealPrice = null, string? DealDescription = null);
public record ImportCategory(string Name, List<ImportItem> Items);
public record MenuImportPreview(List<ImportCategory> Categories);
public record ApplyImportRequest(List<ImportCategory> Categories);

/// <summary>Pulls a structured menu out of a photo (implemented in Infrastructure
/// against a vision model). Returns the same preview shape as the CSV parser.</summary>
public interface IMenuVisionExtractor
{
    Task<MenuImportPreview> Extract(byte[] image, string mimeType, CancellationToken ct = default);
}

/// <summary>
/// Bulk menu import: parse a CSV/spreadsheet, and apply a reviewed preview (create
/// categories + items). Photo extraction is delegated to IMenuVisionExtractor.
/// </summary>
public class MenuImportService
{
    private readonly IAppDbContext _db;
    public MenuImportService(IAppDbContext db) => _db = db;

    /// <summary>Parse "Category,Item,Price" rows (a header row is skipped if present).
    /// Handles quoted fields and prices written like "Rs 1,200".</summary>
    public MenuImportPreview ParseCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) throw new DomainException("The file is empty.");
        var order = new List<string>();
        var map = new Dictionary<string, ImportCategory>(StringComparer.OrdinalIgnoreCase);

        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool first = true;
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var cols = SplitCsvLine(raw);
            if (cols.Count < 3) continue;
            var cat = cols[0].Trim();
            var item = cols[1].Trim();
            var priceText = cols[2].Trim();

            // Skip a header row (non-numeric price on the first data line).
            if (first)
            {
                first = false;
                if (!TryParsePrice(priceText, out _)) continue;
            }
            if (cat.Length == 0 || item.Length == 0) continue;
            TryParsePrice(priceText, out var price);

            // Optional 4th column: a deal/offer price (only kept if below the price).
            decimal? deal = null;
            if (cols.Count >= 4 && TryParsePrice(cols[3], out var d) && d > 0 && d < price) deal = d;

            if (!map.TryGetValue(cat, out var c))
            {
                c = new ImportCategory(cat, new List<ImportItem>());
                map[cat] = c; order.Add(cat);
            }
            c.Items.Add(new ImportItem(item, price, deal));
        }
        var cats = order.Select(k => map[k]).Where(c => c.Items.Count > 0).ToList();
        if (cats.Count == 0) throw new DomainException("No rows found. Use columns: Category, Item, Price.");
        return new MenuImportPreview(cats);
    }

    /// <summary>Create the reviewed categories + items. Existing categories (by name)
    /// are reused; the tenant is stamped automatically on save.</summary>
    public async Task<int> Apply(ApplyImportRequest req, CancellationToken ct = default)
    {
        if (req?.Categories is null || req.Categories.Count == 0) throw new DomainException("Nothing to import.");
        var existing = await _db.Categories.ToListAsync(ct);
        int maxSort = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder);
        int created = 0;

        foreach (var cat in req.Categories.Take(60))
        {
            var name = (cat.Name ?? "").Trim();
            if (name.Length == 0) continue;
            var category = existing.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                category = new MenuCategory { Name = name, SortOrder = ++maxSort };
                _db.Categories.Add(category);
                await _db.SaveChangesAsync(ct);   // materialise id (RestaurantId stamped from tenant)
                existing.Add(category);
            }
            foreach (var it in (cat.Items ?? new List<ImportItem>()).Take(200))
            {
                var iname = (it.Name ?? "").Trim();
                if (iname.Length == 0) continue;
                var price = it.Price < 0 ? 0 : it.Price;
                var dealPrice = it.DealPrice is { } dp && dp >= 0 && dp < price ? dp : (decimal?)null;
                // Keep the title short; the combo contents live in DealDescription (capped
                // to the column's 200 chars). Only meaningful when there's a deal price.
                var dealDesc = dealPrice is null ? "" : (it.DealDescription ?? "").Trim();
                if (dealDesc.Length > 200) dealDesc = dealDesc[..200];
                _db.MenuItems.Add(new MenuItem
                {
                    CategoryId = category.Id, Name = iname,
                    Price = price, Emoji = "🍽️", Available = true,
                    DealPrice = dealPrice,
                    DealDescription = dealDesc,
                });
                created++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return created;
    }

    // --- tiny CSV helpers (avoid a dependency for a 3-column file) ---
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else sb.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static bool TryParsePrice(string text, out decimal price)
    {
        var cleaned = new string((text ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out price);
    }
}
