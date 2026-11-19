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

    private readonly Billing.BillingService? _billing;

    public OrderService(IAppDbContext db, ICurrentTenant tenant, IOrderNotifier notifier,
        IPrintDispatcher print, Inventory.InventoryService inventory, Billing.BillingService? billing = null)
    {
        _db = db; _tenant = tenant; _notifier = notifier; _print = print; _inventory = inventory; _billing = billing;
    }

    private static List<(int, int)> Lines(Order o) => o.Items.Select(i => (i.MenuItemId, i.Qty)).ToList();

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");
    private string Slug => _tenant.Slug ?? "";

    // ---------- place (guest QR self-order, or a waiter ordering on behalf) ----------
    /// <summary>
    /// Places an order for a table (or a parcel/delivery ticket). Every line is priced from the menu as it is now
    /// (portion, live deal price, add-ons), and lines that can't be served are left out. Nothing is saved, not even
    /// a new seating, until at least one line can be served. <paramref name="guestPassId"/> is the guest phone's
    /// pass, kept on the order so only that phone can cancel it.
    /// </summary>
    public async Task<OrderDto> Place(PlaceOrderRequest req, OrderSource source = OrderSource.Qr,
        string? waiterName = null, CancellationToken ct = default, string? guestPassId = null)
    {
        // The subscription plan may be menu-only — then self-ordering is off.
        var restaurant = await _db.Restaurants.FirstAsync(r => r.Id == TenantId, ct);
        if (!restaurant.FeatureOrdering)
            throw new DomainException("Online ordering isn't enabled for this restaurant.");

        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Code == req.TableCode, ct)
            ?? throw new DomainException("Unknown table.");

        if (req.Items is null || req.Items.Count == 0)
            throw new DomainException("Your order is empty.");

        // Only load the handful of items actually on this order (not the whole menu),
        // read-only — we project prices/names and never mutate the menu here.
        var wantedIds = req.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var menu = await _db.MenuItems
            .AsNoTracking()
            .Where(m => wantedIds.Contains(m.Id))
            .Include(m => m.Variants)
            .Include(m => m.ModifierGroups).ThenInclude(g => g.Options)
            .ToListAsync(ct);

        var lines = new List<(int MenuItemId, string Name, string? Variant, decimal UnitPrice, int Qty, string? Note,
            List<(string Name, decimal Price)> Chosen)>();
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

            lines.Add((mi.Id, mi.Name, variantName, basePrice + chosen.Sum(m => m.Price), line.Qty, line.Note, chosen));
        }
        if (lines.Count == 0)
            throw new DomainException("None of these items can be ordered right now. Please choose something else.");

        // Reuse the table's open session, or open a fresh one for this seating.
        var session = await _db.Sessions
            .Where(s => s.DiningTableId == table.Id && s.IsOpen)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(ct);
        if (session is null)
        {
            // A parcel or delivery ticket is one customer's order: once it's settled or cancelled it stays closed.
            if (table.Kind != OrderChannel.Dine)
                throw new DomainException("This order is already closed. Please start a new one.");
            session = TableSession.Open(table);
            _db.Sessions.Add(session);
            await _db.SaveChangesAsync(ct);   // materialise the session id
        }

        var branchName = (await _db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == table.BranchId, ct))?.Name ?? "";
        var order = Order.Place(TenantId,
            new TableSessionInfo(session.Id, table.Id, table.Code, table.Name, table.BranchId, branchName), req.Note, source);
        order.Channel = table.Kind;
        if (source == OrderSource.Qr) order.GuestPassId = guestPassId;

        // A waiter taking the order is the one serving that table — attach them now so
        // it shows on their board and skips auto-assignment later.
        if (source == OrderSource.Waiter && !string.IsNullOrWhiteSpace(waiterName))
            order.AssignWaiter(waiterName.Trim());

        foreach (var l in lines)
            order.AddItem(l.MenuItemId, l.Name, l.Variant, l.UnitPrice, l.Qty, l.Note, l.Chosen);
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
    public async Task<OrderDto> Cook(int id, int eta, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct);
        var skippedApproval = order.Status == OrderStatus.Placed;
        order.StartCooking(eta);
        await _db.SaveChangesAsync(ct);
        var dto = await Publish(order, ct);
        // Cooking straight from Placed skips Approve, which is where stock is taken. Take it here instead,
        // so a later cancel doesn't give back stock that never left.
        if (skippedApproval && await StockEnabled(ct))
            await _inventory.ApplyOrderConsumption(Lines(order), restore: false, ct);
        return dto;
    }
    public Task<OrderDto> Ready(int id, CancellationToken ct = default) => Mutate(id, o => o.MarkReady(), ct);
    public Task<OrderDto> Serve(int id, CancellationToken ct = default) => Mutate(id, o => o.Serve(), ct);
    public Task<OrderDto> Assign(int id, string waiter, CancellationToken ct = default) => Mutate(id, o => o.AssignWaiter(waiter), ct);

    /// <summary>
    /// Cancelling an order that already reached the kitchen returns its ingredients to stock. The owner's settings
    /// change that:
    /// - "no cancelling once cooking starts": food that is cooking or ready can only be cancelled by the owner;
    /// - "cashier can cancel at any stage": the counter (cashier, manager, owner) can cancel any unpaid order,
    ///   served included, whatever the rule above says;
    /// - "keep stock": food cancelled after cooking started stays used up (it was made, or half made, and is
    ///   thrown away). Food that was only approved always goes back, since nobody touched it.
    /// Before the kitchen starts cooking, cancelling works the way it always did.
    /// </summary>
    public async Task<OrderDto> Cancel(int id, string? reason, CancellationToken ct = default, string? by = null,
        Canceller who = default)
    {
        var order = await LoadOrder(id, ct);
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        var anyStage = AnyStage(r, who);
        var started = order.Status is OrderStatus.Cooking or OrderStatus.Ready;
        if (started && r.NoCancelOnceCooking && !who.IsOwner && !anyStage)
            throw new DomainException(who.IsGuest
                ? "Your food is already being made, so this order can't be cancelled now. Please ask the staff."
                : "Cooking has started on this order, so only the owner can cancel it now.");
        var restock = StockComesBack(order.Status, r);
        order.Cancel(string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim(), by, evenServed: anyStage);
        await RefuseIfBillBelowPaid(order, ct);
        await _db.SaveChangesAsync(ct);
        var dto = await Publish(order, ct);
        if (restock)
            await _inventory.ApplyOrderConsumption(Lines(order), restore: true, ct);
        // A takeaway or delivery with nothing left on it is over (its private table is never reused);
        // a dine-in table stays open, since the guests are still sitting there.
        if (await _db.Sessions.AnyAsync(s => s.Id == order.TableSessionId && s.Channel != OrderChannel.Dine, ct))
            await CloseSessionIfSettled(order.TableSessionId, ct);
        return dto;
    }

    /// <summary>Whether cancelling (or taking a dish off) an order in this state gives its ingredients back.</summary>
    private static bool StockComesBack(OrderStatus status, Domain.Restaurants.Restaurant r) => r.StockTrackingEnabled && status switch
    {
        OrderStatus.Approved => true,
        OrderStatus.Cooking or OrderStatus.Ready => !r.KeepStockOnCookedCancel,
        _ => false,   // not taken yet (Placed), or served and eaten
    };

    /// <summary>The owner's cashier option: the counter may cancel any unpaid order, or take off any dish, at any stage.</summary>
    private static bool AnyStage(Domain.Restaurants.Restaurant r, Canceller who) =>
        r.CashierCancelAnyStage && !who.IsGuest && (who.IsOwner || who.AtCounter);

    /// <summary>The cancelling rules a staff screen needs to show only the buttons that will work.</summary>
    public async Task<CancelRulesDto> CancelRules(CancellationToken ct = default) =>
        await _db.Restaurants.AsNoTracking().Where(x => x.Id == TenantId)
            .Select(x => new CancelRulesDto(x.NoCancelOnceCooking, x.CashierCancelAnyStage)).FirstAsync(ct);

    /// <summary>Whether a guest may cancel an order right now, so their screen only offers what will work.</summary>
    public async Task<bool> GuestMayCancel(OrderDto order, CancellationToken ct = default)
    {
        if (order.Status is "Placed" or "Approved") return true;
        if (order.Status != "Cooking") return false;
        return !(await _db.Restaurants.AsNoTracking().Where(x => x.Id == TenantId).Select(x => x.NoCancelOnceCooking).FirstAsync(ct));
    }

    /// <summary>
    /// Part of this table's bill is already paid. A cancel or a dish coming off (already applied to
    /// <paramref name="changed"/>, not saved) must not bring the bill below what was paid: that money would have
    /// nothing to pay for, the bill could never be settled, and an empty table would carry it to the next guests.
    /// The cashier gives the difference back first (Bill → Give back), then makes the change.
    /// </summary>
    private async Task RefuseIfBillBelowPaid(Order changed, CancellationToken ct)
    {
        var paid = (await _db.Payments.Where(p => p.TableSessionId == changed.TableSessionId).Select(p => p.Amount).ToListAsync(ct)).Sum();
        if (paid <= 0) return;
        var open = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .Where(o => o.TableSessionId == changed.TableSessionId && o.Id != changed.Id
                && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        if (changed.Status is not (OrderStatus.Paid or OrderStatus.Cancelled) && changed.Items.Count > 0) open.Add(changed);

        decimal total;
        if (_billing is not null) total = await _billing.StandardTotal(changed.TableSessionId, open, ct);
        else if (open.Count == 0) total = 0;
        else return;   // no bill pricing here (a bare test set-up): only the empty-table case can be judged

        if (paid > total + 0.01m)
        {
            var back = paid - total;
            throw new DomainException(total <= 0
                ? $"Rs {paid:N0} of this table's bill is already paid. Give it back first (Bill → Give back), then cancel."
                : $"Rs {paid:N0} of this table's bill is already paid, and without this the bill comes to Rs {total:N0}. Give back Rs {back:N0} first (Bill → Give back), then try again.");
        }
    }

    /// <summary>
    /// Takes a dish off a running order and keeps a record of it. If it had already gone to the
    /// kitchen and wasn't served, its ingredients go back to stock, the same rule as a cancellation
    /// (including the owner's two cancelling settings; served food counts as cooked).
    /// </summary>
    public async Task<OrderDto> VoidItem(int orderId, int itemId, VoidItemRequest req, string by, CancellationToken ct = default,
        Canceller who = default)
    {
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length == 0) throw new DomainException("Say why this is being removed.");

        var order = await LoadOrder(orderId, ct);
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        if ((order.Status is OrderStatus.Cooking or OrderStatus.Ready or OrderStatus.Served) && r.NoCancelOnceCooking
            && !who.IsOwner && !AnyStage(r, who))
            throw new DomainException("Cooking has started on this order, so only the owner can take a dish off it now.");
        var restock = StockComesBack(order.Status, r);
        var item = order.VoidItem(itemId, req.Qty);
        await RefuseIfBillBelowPaid(order, ct);
        var label = item.VariantName is null ? item.Name : $"{item.Name} ({item.VariantName})";
        _db.OrderVoids.Add(new OrderVoid
        {
            BranchId = order.BranchId, OrderId = order.Id, TableName = order.TableName, ItemName = label,
            Qty = req.Qty, UnitPrice = item.Price, Reason = reason, VoidedBy = by,
        });
        await _db.SaveChangesAsync(ct);
        var dto = await Publish(order, ct);
        if (restock)
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
        // The guests left the old table: passes for it end, and they scan the new table's QR code.
        var oldTable = await _db.Tables.FirstOrDefaultAsync(t => t.Id == source.DiningTableId, ct);
        oldTable?.EndGuestPasses(DateTime.UtcNow);
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
        if (oldTable is not null) await _notifier.TablePassesEnded(Slug, oldTable.Code, ct);
        return moved;
    }

    // Only reads: looking at an order changes nothing, so it isn't sent to every screen again.
    public async Task<OrderDto> GetOrder(int id, CancellationToken ct = default)
        => OrderDto.From(await LoadOrder(id, ct));

    /// <summary>
    /// "Order again": the same dishes, portions and add-ons, priced from today's menu and placed exactly like a new
    /// order (so plan switches, the guest rules and a closed parcel or delivery ticket all apply). Dishes, portions
    /// or add-ons that are no longer on the menu are left out and named.
    /// </summary>
    public async Task<OrderDto> Reorder(int id, CancellationToken ct = default, OrderSource source = OrderSource.Qr,
        string? waiterName = null, string? guestPassId = null)
    {
        var original = await LoadOrder(id, ct);
        if (original.Status is not (OrderStatus.Served or OrderStatus.Paid or OrderStatus.Cancelled))
            throw new DomainException("Can only reorder completed or cancelled orders.");

        var ids = original.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var menu = await _db.MenuItems.AsNoTracking().Where(m => ids.Contains(m.Id))
            .Include(m => m.Variants)
            .Include(m => m.ModifierGroups).ThenInclude(g => g.Options)
            .ToListAsync(ct);

        var lines = new List<OrderLineRequest>();
        var missing = new List<string>();
        foreach (var item in original.Items)
        {
            var mi = menu.FirstOrDefault(m => m.Id == item.MenuItemId);
            if (mi is null || !mi.Available) { missing.Add(item.Name); continue; }

            int? variantId = null;
            if (mi.Variants.Count > 0)
            {
                var v = mi.Variants.FirstOrDefault(x => string.Equals(x.Name, item.VariantName, StringComparison.OrdinalIgnoreCase));
                if (v is null) { missing.Add(item.VariantName is null ? item.Name : $"{item.Name} ({item.VariantName})"); continue; }
                variantId = v.Id;
            }

            // Each add-on by name, from the item's groups as they are now.
            var options = mi.ModifierGroups.SelectMany(g => g.Options).ToList();
            var optionIds = new List<int>();
            var lost = false;
            foreach (var m in item.Modifiers)
            {
                var o = options.FirstOrDefault(x => string.Equals(x.Name, m.Name, StringComparison.OrdinalIgnoreCase));
                if (o is null) { lost = true; break; }
                optionIds.Add(o.Id);
            }
            // The groups' rules may have changed too (a required choice added): then it needs choosing again.
            if (!lost && mi.ModifierGroups.Any(g =>
                    { var n = g.Options.Count(o => optionIds.Contains(o.Id)); return n < g.MinSelect || n > g.MaxSelect; }))
                lost = true;
            if (lost) { missing.Add(item.Name); continue; }

            lines.Add(new OrderLineRequest(mi.Id, item.Qty, item.Note, variantId, optionIds));
        }

        if (lines.Count == 0)
            throw new DomainException(missing.Count > 0
                ? $"These can't be ordered again as they were: {string.Join(", ", missing.Distinct())}. Please order from the menu."
                : "None of the items from this order are currently available.");

        return await Place(new PlaceOrderRequest(original.TableCode, lines, original.CustomerNote), source, waiterName, ct, guestPassId);
    }

    /// <summary>The guest pass that placed an order (null for staff orders), so only that phone can cancel it.</summary>
    public async Task<string?> GuestPassOf(int id, CancellationToken ct = default) =>
        await _db.Orders.AsNoTracking().Where(o => o.Id == id).Select(o => o.GuestPassId).FirstOrDefaultAsync(ct);

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
        // A date with no time is a whole Pakistan day, the same days the reports and day closing use.
        if (fromUtc is DateTime f)
        {
            if (f.TimeOfDay == TimeSpan.Zero) f = BusinessDay.StartUtc(f);
            query = query.Where(o => o.CreatedAt >= f);
        }
        if (toUtc is DateTime t)
        {
            if (t.TimeOfDay == TimeSpan.Zero) { var end = BusinessDay.StartUtc(t.AddDays(1)); query = query.Where(o => o.CreatedAt < end); }
            else query = query.Where(o => o.CreatedAt <= t);
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
