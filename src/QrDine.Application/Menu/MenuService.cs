using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Application.Menu.Dtos;

namespace QrDine.Application.Menu;

/// <summary>Read-side use cases for the guest menu and table list.</summary>
public class MenuService
{
    private readonly IAppDbContext _db;
    public MenuService(IAppDbContext db) => _db = db;

    // The tenant is already resolved by middleware, so the global query filter
    // scopes every query below to the current restaurant automatically.

    public async Task<MenuDto?> GetMenu(string slug, bool forAdmin = false, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (r is null) return null;

        var cats = await _db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ToListAsync(ct);
        var items = await _db.MenuItems.AsNoTracking()
            .Include(i => i.Variants)
            .Include(i => i.ModifierGroups).ThenInclude(g => g.Options)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var menu = cats.Select(c => new MenuCategoryDto(
            c.Id, c.Name,
            items.Where(i => i.CategoryId == c.Id)
                // Guests don't see a time-limited item outside its window; the owner always does.
                .Where(i => forAdmin || !(i.DealHideWhenOff && !i.IsDealActive(now)))
                .Select(i => new MenuItemDto(
                i.Id, i.Name, i.NameUrdu, i.Description, i.Price, i.Emoji, i.ImageUrl, i.Available, i.PrepMinutes,
                i.Variants.OrderBy(v => v.SortOrder).Select(v => new VariantDto(v.Id, v.Name, v.Price)).ToList(),
                i.ModifierGroups.OrderBy(g => g.SortOrder).Select(g => new ModifierGroupDto(
                    g.Id, g.Name, g.MinSelect, g.MaxSelect,
                    g.Options.OrderBy(o => o.SortOrder).Select(o => new ModifierOptionDto(o.Id, o.Name, o.Price)).ToList())).ToList(),
                i.TaxRate, i.FurtherTaxRate, i.PctCode,
                i.DealPrice, i.IsDealActive(now), i.DealDescription,
                i.DealDays, i.DealStartMinutes, i.DealEndMinutes, i.DealHideWhenOff
            )).ToList()))
            .ToList();

        var dto = new RestaurantDto(r.Slug, r.Name, r.Currency,
            new FeaturesDto(r.FeatureOrdering, r.FeatureKitchen, r.FeatureWaiter, r.FeatureBilling), r.LogoUrl);
        return new MenuDto(dto, menu);
    }

    public async Task<IReadOnlyList<TableDto>> GetTables(CancellationToken ct = default)
    {
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var tables = await _db.Tables.AsNoTracking().Where(t => t.Kind == Domain.Ordering.OrderChannel.Dine).OrderBy(t => t.Code).ToListAsync(ct);
        return tables.Select(t => new TableDto(t.Code, t.Name, branches.GetValueOrDefault(t.BranchId, ""))).ToList();
    }
}
