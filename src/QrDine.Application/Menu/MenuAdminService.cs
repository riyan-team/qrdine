using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Application.Menu.Dtos;
using QrDine.Domain.Common;
using QrDine.Domain.Menu;

namespace QrDine.Application.Menu;

/// <summary>
/// Owner-facing menu management: categories and items, including their variants
/// and modifier groups. Editing an item replaces its variants/modifiers wholesale
/// — simple and safe, because orders snapshot names/prices at order time and never
/// point back at these rows.
/// </summary>
public class MenuAdminService
{
    private readonly IAppDbContext _db;
    public MenuAdminService(IAppDbContext db) => _db = db;

    /// <summary>
    /// Changes every price by a percentage, for when costs go up. Sizes change with their dish,
    /// and a running deal keeps its own price until the owner changes it.
    /// </summary>
    public async Task<int> BulkPrice(BulkPriceRequest input, CancellationToken ct = default)
    {
        var query = _db.MenuItems.Include(m => m.Variants).AsQueryable();
        if (input.CategoryId is int categoryId) query = query.Where(m => m.CategoryId == categoryId);
        var items = await query.ToListAsync(ct);

        decimal Adjust(decimal price)
        {
            var next = price * (1 + input.Percent / 100m);
            if (input.RoundTo > 0) next = Math.Round(next / input.RoundTo, 0, MidpointRounding.AwayFromZero) * input.RoundTo;
            return Math.Max(0, Math.Round(next, 2));
        }

        foreach (var item in items)
        {
            item.Price = Adjust(item.Price);
            foreach (var v in item.Variants) v.Price = Adjust(v.Price);
        }
        await _db.SaveChangesAsync(ct);
        return items.Count;
    }

    // ---- categories ----
    public async Task<int> AddCategory(CategoryInput input, CancellationToken ct = default)
    {
        var c = new MenuCategory { Name = input.Name.Trim(), SortOrder = input.SortOrder };
        _db.Categories.Add(c);
        await _db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task UpdateCategory(int id, CategoryInput input, CancellationToken ct = default)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Category not found.");
        c.Name = input.Name.Trim();
        c.SortOrder = input.SortOrder;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteCategory(int id, CancellationToken ct = default)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Category not found.");
        if (await _db.MenuItems.AnyAsync(i => i.CategoryId == id, ct))
            throw new DomainException("Move or delete this category's items first.");
        _db.Categories.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    // ---- items ----
    public async Task<int> CreateItem(ItemInput input, CancellationToken ct = default)
    {
        var item = new MenuItem();
        Apply(item, input);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task UpdateItem(int id, ItemInput input, CancellationToken ct = default)
    {
        var item = await _db.MenuItems
            .Include(i => i.Variants)
            .Include(i => i.ModifierGroups).ThenInclude(g => g.Options)
            .FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new DomainException("Item not found.");

        // Rebuild children — EF deletes the orphans on save (cascade).
        item.Variants.Clear();
        item.ModifierGroups.Clear();
        Apply(item, input);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetItemPhoto(int id, string imageUrl, CancellationToken ct = default)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new DomainException("Item not found.");
        item.ImageUrl = imageUrl;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetAvailability(int id, bool available, CancellationToken ct = default)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new DomainException("Item not found.");
        item.Available = available;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteItem(int id, CancellationToken ct = default)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new DomainException("Item not found.");
        _db.MenuItems.Remove(item);   // cascades to its variants/modifiers; orders keep their snapshot
        await _db.SaveChangesAsync(ct);
    }

    private static void Apply(MenuItem item, ItemInput input)
    {
        item.CategoryId = input.CategoryId;
        item.Name = input.Name.Trim();
        item.NameUrdu = input.NameUrdu?.Trim() ?? "";
        item.Description = input.Description?.Trim() ?? "";
        item.Price = input.Price;
        item.Emoji = string.IsNullOrWhiteSpace(input.Emoji) ? "🍽️" : input.Emoji.Trim();
        item.Available = input.Available;
        item.PrepMinutes = Math.Clamp(input.PrepMinutes, 1, 180);
        // Per-item tax (null rate = use the restaurant default at bill time).
        item.TaxRate = input.TaxRate is { } tr ? Math.Clamp(tr, 0, 100) : null;
        item.FurtherTaxRate = Math.Clamp(input.FurtherTaxRate, 0, 100);
        item.PctCode = input.PctCode?.Trim() ?? "";

        // Deal / offer. A deal price at/above the normal price is meaningless — drop it.
        item.DealPrice = input.DealPrice is { } dp && dp >= 0 && dp < input.Price ? dp : null;
        item.DealDescription = input.DealDescription?.Trim() ?? "";
        item.DealDays = (input.DealDays ?? "").Trim();
        item.DealStartMinutes = input.DealStartMinutes;
        item.DealEndMinutes = input.DealEndMinutes;
        item.DealHideWhenOff = input.DealHideWhenOff && item.DealPrice is not null;

        foreach (var v in input.Variants ?? new())
            item.Variants.Add(new MenuItemVariant { Name = v.Name.Trim(), Price = v.Price, SortOrder = v.SortOrder });

        foreach (var g in input.ModifierGroups ?? new())
        {
            var group = new ModifierGroup
            {
                Name = g.Name.Trim(),
                MinSelect = Math.Max(0, g.MinSelect),
                MaxSelect = Math.Max(1, g.MaxSelect),
                SortOrder = g.SortOrder,
            };
            foreach (var o in g.Options ?? new())
                group.Options.Add(new ModifierOption { Name = o.Name.Trim(), Price = o.Price, SortOrder = o.SortOrder });
            item.ModifierGroups.Add(group);
        }
    }
}
