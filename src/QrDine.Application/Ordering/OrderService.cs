using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Printing;
using QrDine.Domain.Common;
using QrDine.Domain.Ordering;
using QrDine.Domain.Tables;

namespace QrDine.Application.Ordering;

/// <summary>
/// Orchestrates the order lifecycle. It coordinates the DbContext, the tenant,
/// and the live notifier, but the *rules* live in the Order aggregate — this
/// class never sets a status directly.
/// </summary>
public class OrderService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IOrderNotifier _notifier;
    private readonly IPrintDispatcher _print;
    private readonly Inventory.InventoryService _inventory;

    public OrderService(IAppDbContext db, ICurrentTenant tenant, IOrderNotifier notifier,
        IPrintDispatcher print, Inventory.InventoryService inventory)
    {
        _db = db; _tenant = tenant; _notifier = notifier; _print = print; _inventory = inventory;
    }

    private static List<(int, int)> Lines(Order o) => o.Items.Select(i => (i.MenuItemId, i.Qty)).ToList();

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");
    private string Slug => _tenant.Slug ?? "";

    // ---------- place (guest) ----------
    public async Task<OrderDto> Place(PlaceOrderRequest req, CancellationToken ct = default)
    {
        // The subscription plan may be menu-only — then self-ordering is off.
        var restaurant = await _db.Restaurants.FirstAsync(r => r.Id == TenantId, ct);
        if (!restaurant.FeatureOrdering)
            throw new DomainException("Online ordering isn't enabled for this restaurant.");

        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Code == req.TableCode, ct)
            ?? throw new DomainException("Unknown table.");

        if (req.Items is null || req.Items.Count == 0)
            throw new DomainException("Your order is empty.");

        // Reuse the table's open session, or open a fresh one for this seating.
        var session = await _db.Sessions
            .Where(s => s.DiningTableId == table.Id && s.IsOpen)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(ct);
        if (session is null)
        {
            session = TableSession.Open(table);
            _db.Sessions.Add(session);
            await _db.SaveChangesAsync(ct);   // materialise the session id
        }

        var branchName = (await _db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == table.BranchId, ct))?.Name ?? "";
        var order = Order.Place(TenantId,
            new TableSessionInfo(session.Id, table.Id, table.Code, table.Name, table.BranchId, branchName), req.Note);

        var menu = await _db.MenuItems
            .Include(m => m.Variants)
            .Include(m => m.ModifierGroups).ThenInclude(g => g.Options)
            .ToListAsync(ct);

        foreach (var line in req.Items)
        {
            var mi = menu.FirstOrDefault(m => m.Id == line.MenuItemId);
            if (mi is null || !mi.Available) continue;   // skip unknown / out-of-stock

            // Resolve the portion: if the item has variants, one must be chosen.
            decimal basePrice = mi.Price;
            string? variantName = null;
            if (mi.Variants.Count > 0)
            {
                var v = mi.Variants.FirstOrDefault(x => x.Id == line.VariantId)
                    ?? throw new DomainException($"Please choose a size for {mi.Name}.");
                basePrice = v.Price; variantName = v.Name;
            }

            // Resolve modifiers and enforce each group's min/max rule.
            var chosenIds = line.ModifierOptionIds ?? new List<int>();
            var chosen = new List<(string Name, decimal Price)>();
            foreach (var group in mi.ModifierGroups)
            {
                var picked = group.Options.Where(o => chosenIds.Contains(o.Id)).ToList();
                if (picked.Count < group.MinSelect || picked.Count > group.MaxSelect)
                    throw new DomainException(
                        $"{mi.Name} — choose {group.Name} ({group.MinSelect}–{group.MaxSelect}).");
                chosen.AddRange(picked.Select(o => (o.Name, o.Price)));
            }

            var unitPrice = basePrice + chosen.Sum(m => m.Price);
            order.AddItem(mi.Id, mi.Name, variantName, unitPrice, line.Qty, line.Note, chosen);
        }
        order.ConfirmPlaced();

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return await Publish(order, ct);
    }

    // ---------- transitions (staff) ----------
    // Approve sends the order to the kitchen — prints its ticket and consumes stock.
    public async Task<OrderDto> Approve(int id, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct);
        order.Approve();
        await AutoAssignWaiter(order, ct);
        await _db.SaveChangesAsync(ct);
        var dto = await Publish(order, ct);
        await _print.Dispatch(Slug, PrintJobs.KitchenTicket(dto), ct);
        await _inventory.ApplyOrderConsumption(Lines(order), restore: false, ct);
        return dto;
    }

    /// <summary>If the restaurant runs auto-assignment, give this order to the
    /// waiter currently handling the fewest active orders. No-op in manual mode
    /// (waiters take their own tables) or when there are no waiters on staff.</summary>
    private async Task AutoAssignWaiter(Order order, CancellationToken ct)
    {
        if (order.WaiterName is not null) return;
        var restaurant = await _db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == TenantId, ct);
        if (!restaurant.WaiterAutoAssign) return;

        var waiters = await _db.Staff.AsNoTracking()
            .Where(s => s.Role == Domain.Identity.StaffRole.Waiter)
            .Select(s => s.DisplayName).ToListAsync(ct);
        if (waiters.Count == 0) return;

        // Current active load per waiter (orders not yet paid/cancelled).
        var load = await _db.Orders.AsNoTracking()
            .Where(o => o.WaiterName != null && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.WaiterName!)
            .Select(g => new { Waiter = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Waiter, x => x.Count, ct);

        var pick = waiters.OrderBy(w => load.GetValueOrDefault(w, 0)).ThenBy(w => w).First();
        order.AssignWaiter(pick);
    }
    public Task<OrderDto> Cook(int id, int eta, CancellationToken ct = default) => Mutate(id, o => o.StartCooking(eta), ct);
    public Task<OrderDto> Ready(int id, CancellationToken ct = default) => Mutate(id, o => o.MarkReady(), ct);
    public Task<OrderDto> Serve(int id, CancellationToken ct = default) => Mutate(id, o => o.Serve(), ct);
    public Task<OrderDto> Assign(int id, string waiter, CancellationToken ct = default) => Mutate(id, o => o.AssignWaiter(waiter), ct);

    // Cancelling an order that already reached the kitchen returns its ingredients to stock.
    public async Task<OrderDto> Cancel(int id, string? reason, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct);
        var wasConsumed = order.Status is OrderStatus.Approved or OrderStatus.Cooking or OrderStatus.Ready;
        order.Cancel(reason ?? "unspecified");
        await _db.SaveChangesAsync(ct);
        var dto = await Publish(order, ct);
        if (wasConsumed) await _inventory.ApplyOrderConsumption(Lines(order), restore: true, ct);
        return dto;
    }

    public async Task<OrderDto> Pay(int id, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct);
        order.Pay();
        await _db.SaveChangesAsync(ct);
        await CloseSessionIfSettled(order.TableSessionId, ct);
        return await Publish(order, ct);
    }

    // ---------- queries ----------
    public async Task<IReadOnlyList<OrderDto>> StaffOrders(CancellationToken ct = default)
    {
        var orders = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .Where(o => o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.CreatedAt).ToListAsync(ct);
        return orders.Select(OrderDto.From).ToList();
    }

    public async Task<IReadOnlyList<OrderDto>> TableOrders(string tableCode, CancellationToken ct = default)
    {
        var orders = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .Where(o => o.TableCode == tableCode
                && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.CreatedAt).ToListAsync(ct);
        return orders.Select(OrderDto.From).ToList();
    }

    // ---------- helpers ----------
    private async Task<Order> LoadOrder(int id, CancellationToken ct)
        => await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Modifiers).FirstOrDefaultAsync(o => o.Id == id, ct)
           ?? throw new DomainException("Order not found.");

    private async Task<OrderDto> Mutate(int id, Action<Order> act, CancellationToken ct)
    {
        var order = await LoadOrder(id, ct);
        act(order);
        await _db.SaveChangesAsync(ct);
        return await Publish(order, ct);
    }

    private async Task CloseSessionIfSettled(int sessionId, CancellationToken ct)
    {
        var stillOpen = await _db.Orders.AnyAsync(o => o.TableSessionId == sessionId
            && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled, ct);
        if (stillOpen) return;
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is { IsOpen: true }) { session.Close(); await _db.SaveChangesAsync(ct); }
    }

    private async Task<OrderDto> Publish(Order order, CancellationToken ct)
    {
        var dto = OrderDto.From(order);
        await _notifier.OrderChanged(Slug, dto, ct);
        return dto;
    }
}
