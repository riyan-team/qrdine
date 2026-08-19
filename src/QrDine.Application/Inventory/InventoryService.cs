using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Inventory;

namespace QrDine.Application.Inventory;

/// <summary>
/// Stock management and recipe-based auto-deduction. When an order is approved,
/// each item's recipe consumes its ingredients; a cancel after approval restores
/// them. Every quantity change writes a StockMovement for a full audit trail.
/// </summary>
public class InventoryService
{
    private readonly IAppDbContext _db;
    public InventoryService(IAppDbContext db) => _db = db;

    // ---- stock items ----
    public async Task<IReadOnlyList<StockItemDto>> ListStock(CancellationToken ct = default) =>
        await _db.Stock.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new StockItemDto(s.Id, s.Name, s.Unit, s.Quantity, s.LowThreshold, s.Quantity <= s.LowThreshold))
            .ToListAsync(ct);

    public async Task<int> CreateStockItem(StockItemInput input, CancellationToken ct = default)
    {
        var s = new StockItem { Name = input.Name.Trim(), Unit = input.Unit.Trim(), Quantity = input.Quantity, LowThreshold = input.LowThreshold };
        _db.Stock.Add(s);
        await _db.SaveChangesAsync(ct);
        if (input.Quantity != 0) await Move(s.Id, input.Quantity, StockMovementType.Restock, "Opening stock", ct);
        return s.Id;
    }

    public async Task UpdateStockItem(int id, StockItemInput input, CancellationToken ct = default)
    {
        var s = await Get(id, ct);
        s.Name = input.Name.Trim(); s.Unit = input.Unit.Trim(); s.LowThreshold = input.LowThreshold;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteStockItem(int id, CancellationToken ct = default)
    {
        var s = await Get(id, ct);
        _db.Stock.Remove(s);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Restock(int id, decimal qty, string? reason, CancellationToken ct = default)
    {
        var s = await Get(id, ct);
        s.Quantity += qty;
        await Move(id, qty, StockMovementType.Restock, reason ?? "Restock", ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Adjust(int id, decimal newQty, string? reason, CancellationToken ct = default)
    {
        var s = await Get(id, ct);
        var delta = newQty - s.Quantity;
        s.Quantity = newQty;
        await Move(id, delta, StockMovementType.Adjust, reason ?? "Manual adjustment", ct);
        await _db.SaveChangesAsync(ct);
    }

    // ---- recipes ----
    public async Task<IReadOnlyList<RecipeLineDto>> GetRecipe(int menuItemId, CancellationToken ct = default)
    {
        var item = await _db.MenuItems.Include(m => m.Recipe).FirstOrDefaultAsync(m => m.Id == menuItemId, ct)
            ?? throw new DomainException("Item not found.");
        var stock = await _db.Stock.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        return item.Recipe.Where(r => stock.ContainsKey(r.StockItemId))
            .Select(r => new RecipeLineDto(r.StockItemId, stock[r.StockItemId].Name, stock[r.StockItemId].Unit, r.QtyPerServing))
            .ToList();
    }

    public async Task SetRecipe(int menuItemId, RecipeInput input, CancellationToken ct = default)
    {
        var item = await _db.MenuItems.Include(m => m.Recipe).FirstOrDefaultAsync(m => m.Id == menuItemId, ct)
            ?? throw new DomainException("Item not found.");
        item.Recipe.Clear();
        foreach (var l in input.Lines ?? new())
            if (l.QtyPerServing > 0) item.Recipe.Add(new RecipeLine { StockItemId = l.StockItemId, QtyPerServing = l.QtyPerServing });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MovementDto>> RecentMovements(int take = 40, CancellationToken ct = default)
    {
        var names = await _db.Stock.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return await _db.StockMovements.AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(take)
            .Select(m => new MovementDto(names.ContainsKey(m.StockItemId) ? names[m.StockItemId] : "(deleted)", m.Delta, m.Type.ToString(), m.Reason, m.CreatedAt))
            .ToListAsync(ct);
    }

    // ---- auto-deduction (called by OrderService) ----
    /// <summary>Consume (restore=false) or return (restore=true) ingredients for an order's items.</summary>
    public async Task ApplyOrderConsumption(IReadOnlyList<(int MenuItemId, int Qty)> lines, bool restore, CancellationToken ct = default)
    {
        if (lines.Count == 0) return;
        var itemIds = lines.Select(l => l.MenuItemId).Distinct().ToList();
        var recipes = await _db.MenuItems.Include(m => m.Recipe)
            .Where(m => itemIds.Contains(m.Id)).ToListAsync(ct);
        if (recipes.All(m => m.Recipe.Count == 0)) return;

        var stockNeeded = new Dictionary<int, decimal>();
        foreach (var line in lines)
        {
            var recipe = recipes.FirstOrDefault(m => m.Id == line.MenuItemId)?.Recipe;
            if (recipe is null) continue;
            foreach (var r in recipe)
                stockNeeded[r.StockItemId] = stockNeeded.GetValueOrDefault(r.StockItemId) + r.QtyPerServing * line.Qty;
        }
        if (stockNeeded.Count == 0) return;

        var stockItems = await _db.Stock.Where(s => stockNeeded.Keys.Contains(s.Id)).ToListAsync(ct);
        foreach (var s in stockItems)
        {
            var amount = stockNeeded[s.Id];
            var delta = restore ? amount : -amount;
            s.Quantity += delta;
            await Move(s.Id, delta, restore ? StockMovementType.Void : StockMovementType.Sale, restore ? "Order cancelled" : "Order to kitchen", ct);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<StockItem> Get(int id, CancellationToken ct) =>
        await _db.Stock.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new DomainException("Stock item not found.");

    private Task Move(int stockItemId, decimal delta, StockMovementType type, string reason, CancellationToken ct)
    {
        _db.StockMovements.Add(new StockMovement { StockItemId = stockItemId, Delta = delta, Type = type, Reason = reason });
        return Task.CompletedTask;
    }
}
