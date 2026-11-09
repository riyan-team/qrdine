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

    // ---------- place (guest QR self-order, or a waiter ordering on behalf) ----------
    public async Task<OrderDto> Place(PlaceOrderRequest req, OrderSource source = OrderSource.Qr,
        string? waiterName = null, CancellationToken ct = default)
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
            new TableSessionInfo(session.Id, table.Id, table.Code, table.Name, table.BranchId, branchName), req.Note, source);
        order.Channel = table.Kind;

        // A waiter taking the order is the one serving that table — attach them now so
        // it shows on their board and skips auto-assignment later.
        if (source == OrderSource.Waiter && !string.IsNullOrWhiteSpace(waiterName))
            order.AssignWaiter(waiterName.Trim());

        // Only load the handful of items actually on this order (not the whole menu),
        // read-only — we project prices/names and never mutate the menu here.
        var wantedIds = req.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var menu = await _db.MenuItems
            .AsNoTracking()
            .Where(m => wantedIds.Contains(m.Id))
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
            else if (mi.IsDealActive(DateTime.UtcNow))
            {
                basePrice = mi.DealPrice!.Value;   // charge the live deal/offer price
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
        if (await StockEnabled(ct))
            await _inventory.ApplyOrderConsumption(Lines(order), restore: false, ct);
        return dto;
    }

    // Whether recipe-based stock deduction is switched on for this restaurant.
    private async Task<bool> StockEnabled(CancellationToken ct) =>
        (await _db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == TenantId, ct)).StockTrackingEnabled;

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
        if (wasConsumed && await StockEnabled(ct))
            await _inventory.ApplyOrderConsumption(Lines(order), restore: true, ct);
        return dto;
    }

    /// <summary>
    /// Takes a dish off a running order and keeps a record of it. If it had already gone to the
    /// kitchen and wasn't served, its ingredients go back to stock, the same rule as a cancellation.
    /// </summary>
    public async Task<OrderDto> VoidItem(int orderId, int itemId, VoidItemRequest req, string by, CancellationToken ct = default)
    {
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length == 0) throw new DomainException("Say why this is being removed.");

        var order = await LoadOrder(orderId, ct);
        var wasConsumed = order.Status is OrderStatus.Approved or OrderStatus.Cooking or OrderStatus.Ready;
        var item = order.VoidItem(itemId, req.Qty);
        var label = item.VariantName is null ? item.Name : $"{item.Name} ({item.VariantName})";
        _db.OrderVoids.Add(new OrderVoid
        {
            BranchId = order.BranchId, OrderId = order.Id, TableName = order.TableName, ItemName = label,
            Qty = req.Qty, UnitPrice = item.Price, Reason = reason, VoidedBy = by,
        });
        await _db.SaveChangesAsync(ct);
        var dto = await Publish(order, ct);
        if (wasConsumed && await StockEnabled(ct))
            await _inventory.ApplyOrderConsumption(new List<(int, int)> { (item.MenuItemId, req.Qty) }, restore: true, ct);
        return dto;
    }

    /// <summary>
    /// Shifts the guests at one table to another. If the other table already has a running bill the
    /// two join into one, along with anything already paid and any rewards member. Otherwise the
    /// seating simply moves. Nothing about the orders themselves changes.
    /// </summary>
    public async Task<IReadOnlyList<OrderDto>> MoveTable(string fromCode, string toCode, CancellationToken ct = default)
    {
        var from = (fromCode ?? "").Trim();
        var to = (toCode ?? "").Trim();
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) throw new DomainException("Choose a different table.");

        var target = await _db.Tables.FirstOrDefaultAsync(t => t.Code == to && t.Kind == OrderChannel.Dine, ct)
            ?? throw new DomainException("That table doesn't exist.");
        var source = await _db.Sessions.Where(s => s.TableCode == from && s.IsOpen).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("This table has no running bill to move.");
        if (source.Channel != OrderChannel.Dine) throw new DomainException("Parcel and delivery tickets stay on their own ticket.");
        if (source.BranchId != target.BranchId) throw new DomainException("That table is in another branch.");

        var orders = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .Where(o => o.TableSessionId == source.Id && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        if (orders.Count == 0) throw new DomainException("This table has no running bill to move.");

        var existing = await _db.Sessions.Where(s => s.DiningTableId == target.Id && s.IsOpen).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct);
        var session = existing ?? source;
        if (existing is not null)
        {
            var paid = await _db.Payments.Where(p => p.TableSessionId == source.Id).ToListAsync(ct);
            foreach (var p in paid) p.TableSessionId = existing.Id;
            existing.LoyaltyMemberId ??= source.LoyaltyMemberId;
            source.Close();
        }
        else
        {
            source.DiningTableId = target.Id;
            source.TableCode = target.Code;
            source.TableName = target.Name;
        }

        var branchName = (await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == target.BranchId, ct))?.Name ?? "";
        var info = new TableSessionInfo(session.Id, target.Id, target.Code, target.Name, target.BranchId, branchName);
        foreach (var o in orders) o.MoveTo(info);
        await _db.SaveChangesAsync(ct);

        var moved = new List<OrderDto>();
        foreach (var o in orders)
        {
            var dto = await Publish(o, ct);
            await _notifier.OrderMoved(Slug, from, dto, ct);
            moved.Add(dto);
        }
        return moved;
    }

    public async Task<OrderDto> Pay(int id, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct);
        order.Pay();
        await _db.SaveChangesAsync(ct);
        await CloseSessionIfSettled(order.TableSessionId, ct);
        return await Publish(order, ct);
    }

    // Only reads: looking at an order changes nothing, so it isn't sent to every screen again.
    public async Task<OrderDto> GetOrder(int id, CancellationToken ct = default)
        => OrderDto.From(await LoadOrder(id, ct));

    public async Task<OrderDto> Reorder(int id, CancellationToken ct = default)
    {
        var original = await LoadOrder(id, ct);
        if (original.Status is not (OrderStatus.Served or OrderStatus.Paid or OrderStatus.Cancelled))
            throw new DomainException("Can only reorder completed or cancelled orders.");

        var restaurant = await _db.Restaurants.FirstAsync(r => r.Id == TenantId, ct);
        if (!restaurant.FeatureOrdering)
            throw new DomainException("Online ordering isn't enabled for this restaurant.");

        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Code == original.TableCode, ct)
            ?? throw new DomainException("Unknown table.");

        var session = await _db.Sessions
            .Where(s => s.DiningTableId == table.Id && s.IsOpen)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(ct);
        if (session is null)
        {
            session = TableSession.Open(table);
            _db.Sessions.Add(session);
            await _db.SaveChangesAsync(ct);
        }

        var branchName = (await _db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == table.BranchId, ct))?.Name ?? "";
        var newOrder = Order.Place(TenantId,
            new TableSessionInfo(session.Id, table.Id, table.Code, table.Name, table.BranchId, branchName),
            original.CustomerNote, OrderSource.Qr);

        var menu = await _db.MenuItems.AsNoTracking().ToListAsync(ct);
        var addedCount = 0;
        var unavailableNames = new List<string>();

        foreach (var item in original.Items)
        {
            var mi = menu.FirstOrDefault(m => m.Id == item.MenuItemId);
            if (mi is null || !mi.Available)
            {
                unavailableNames.Add(item.Name);
                continue;
            }

            newOrder.AddItem(item.MenuItemId, item.Name, item.VariantName, item.Price, item.Qty, item.Note,
                item.Modifiers.Select(m => (m.Name, m.Price)));
            addedCount++;
        }

        if (addedCount == 0)
        {
            var msg = unavailableNames.Count > 0
                ? $"The following item(s) are currently unavailable: {string.Join(", ", unavailableNames.Distinct())}."
                : "None of the items from this order are currently available.";
            throw new DomainException(msg);
        }

        newOrder.ConfirmPlaced();

        _db.Orders.Add(newOrder);
        await _db.SaveChangesAsync(ct);
        return await Publish(newOrder, ct);
    }

    // ---------- queries ----------
    public async Task<IReadOnlyList<OrderDto>> StaffOrders(CancellationToken ct = default)
    {
        var orders = await _db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .Where(o => o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.CreatedAt).ToListAsync(ct);
        return orders.Select(OrderDto.From).ToList();
    }

    /// <summary>Paginated order history for staff dashboards — filter by status, date
    /// range and a search term (table or waiter). Newest first.</summary>
    public async Task<OrderHistoryPage> OrdersHistory(string? status, DateTime? fromUtc, DateTime? toUtc,
        string? q, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Modifiers).AsQueryable();
        if (Enum.TryParse<OrderStatus>(status, true, out var st)) query = query.Where(o => o.Status == st);
        if (fromUtc is DateTime f) query = query.Where(o => o.CreatedAt >= f);
        if (toUtc is DateTime t)
        {
            // A date with no time component means "up to the end of that day".
            if (t.TimeOfDay == TimeSpan.Zero) t = t.AddDays(1).AddSeconds(-1);
            query = query.Where(o => o.CreatedAt <= t);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(o => o.TableCode.Contains(term) || o.TableName.Contains(term)
                || (o.WaiterName != null && o.WaiterName.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        return new OrderHistoryPage(orders.Select(OrderDto.From).ToList(), page, pageSize, total, totalPages);
    }

    public async Task<IReadOnlyList<OrderDto>> TableOrders(string tableCode, CancellationToken ct = default)
    {
        var orders = await _db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Modifiers)
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
