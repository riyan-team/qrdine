using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QrDine.Application.Common;
using QrDine.Application.Loyalty;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Printing;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Customers;
using QrDine.Domain.Ordering;
using QrDine.Domain.Restaurants;

namespace QrDine.Application.Billing;

/// <summary>
/// Produces a live bill for a table and settles it into a numbered invoice.
/// Settling marks every open order in the session paid, closes the seating, and
/// (when FBR is enabled) files the invoice via <see cref="FiscalSyncService"/>.
/// Per-item tax is resolved and snapshotted onto each invoice line at settle time.
/// </summary>
public class BillingService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IOrderNotifier _notifier;
    private readonly FiscalSyncService _fiscalSync;
    private readonly IPrintDispatcher _print;
    private readonly LoyaltyService _loyalty;
    private readonly ILogger<BillingService> _log;
    private readonly FloorTurn? _turn;

    public BillingService(IAppDbContext db, ICurrentTenant tenant, IOrderNotifier notifier,
        FiscalSyncService fiscalSync, IPrintDispatcher print, LoyaltyService loyalty, ILogger<BillingService> log,
        FloorTurn? turn = null)
    {
        _db = db; _tenant = tenant; _notifier = notifier; _fiscalSync = fiscalSync; _print = print;
        _loyalty = loyalty; _log = log; _turn = turn;
    }

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");
    private string Slug => _tenant.Slug ?? "";

    private async Task<List<Order>> OpenOrders(int sessionId, CancellationToken ct) =>
        await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .Where(o => o.TableSessionId == sessionId
                && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);

    private async Task<int?> OpenSessionId(string tableCode, CancellationToken ct)
    {
        var s = await _db.Sessions.Where(x => x.TableCode == tableCode && x.IsOpen)
            .OrderByDescending(x => x.OpenedAt).FirstOrDefaultAsync(ct);
        return s?.Id;
    }

    // Build invoice lines from open orders, resolving each item's tax (its own rate,
    // or the restaurant default) and PCT code — snapshotted onto the line.
    private async Task<List<InvoiceLine>> BuildLines(IEnumerable<Order> orders, Restaurant r, Domain.Tables.TableSession? session,
        string? method, CancellationToken ct)
    {
        var defaultRate = DefaultTaxRate(r, method);
        var items = orders.SelectMany(o => o.Items).ToList();
        var menuIds = items.Select(i => i.MenuItemId).Distinct().ToList();
        var menu = (await _db.MenuItems.AsNoTracking()
                .Where(m => menuIds.Contains(m.Id))
                .Select(m => new { m.Id, m.TaxRate, m.FurtherTaxRate, m.PctCode })
                .ToListAsync(ct))
            .ToDictionary(m => m.Id);

        var lines = items
            .GroupBy(i => new
            {
                i.MenuItemId,
                Name = i.VariantName is null ? i.Name : $"{i.Name} ({i.VariantName})",
                i.Price,
            })
            .Select(g =>
            {
                menu.TryGetValue(g.Key.MenuItemId, out var mi);
                // Tax OFF → no tax at all. Tax ON → the item's own rate if it has one (0 means the item is
                // tax-free), otherwise (blank) the restaurant's default GST rate applies to the item. When the owner
                // says so, a card payment's lower rate replaces an item's own rate too; tax-free stays tax-free.
                // "One tax for all items": every item at the bill's rate, whatever its own (tax-free included).
                var taxRate = !r.TaxEnabled ? 0
                    : r.OneTaxForAllItems ? defaultRate
                    : mi?.TaxRate is decimal own ? (own > 0 && CardRateReplacesOwn(r, method) ? defaultRate : own)
                    : defaultRate;
                return new InvoiceLine
                {
                    ItemCode = g.Key.MenuItemId.ToString(),
                    Name = g.Key.Name,
                    PctCode = mi?.PctCode ?? "",
                    UnitPrice = g.Key.Price,
                    Qty = g.Sum(i => i.Qty),
                    TaxRate = taxRate,
                    FurtherTaxRate = r.TaxEnabled ? (mi?.FurtherTaxRate ?? 0) : 0,
                };
            })
            .OrderBy(l => l.Name)
            .ToList();

        // What the customer pays to have it brought. Untaxed, and not a dish, so reports leave it out of item sales.
        if (session is { DeliveryCharge: > 0 })
            lines.Add(new InvoiceLine
            {
                ItemCode = InvoiceLine.DeliveryCode, Name = "Delivery charge",
                UnitPrice = session.DeliveryCharge, Qty = 1,
            });
        return lines;
    }

    /// <summary>The lower rate applies to card and wallet payments when the owner has set one. A bill paid in several
    /// parts keeps the standard rate, because the total has to stay put while the parts come in.</summary>
    private static decimal DefaultTaxRate(Restaurant r, string? method) =>
        r.TaxPercentCard is decimal card && IsCashless(method) ? card : r.TaxPercent;

    private static bool CardRateReplacesOwn(Restaurant r, string? method) =>
        r.CardRateForAllItems && r.TaxPercentCard is decimal && IsCashless(method);

    /// <summary>The owner's bill options for this seating, priced at the rate the items were priced at.</summary>
    private static Invoice.Charges ChargesFor(Restaurant r, Domain.Tables.TableSession? session, string? rateMethod) => new(
        ServicePercent(r, session),
        ServiceAfterDiscount: r.ServiceAfterDiscount,
        ServiceTaxRate: r.TaxEnabled && r.ServiceChargeTaxable ? DefaultTaxRate(r, rateMethod) : 0,
        RoundToRupee: r.RoundTotals);

    private static bool IsCashless(string? method) =>
        method is not null
        && !method.Equals("Cash", StringComparison.OrdinalIgnoreCase)
        && !method.Equals(KhataMethod, StringComparison.OrdinalIgnoreCase)
        && !method.StartsWith("Split", StringComparison.OrdinalIgnoreCase);

    public const string KhataMethod = "Khata";

    /// <summary>Service charge is for sitting down: parcel and delivery don't pay it.</summary>
    private static decimal ServicePercent(Restaurant r, Domain.Tables.TableSession? session) =>
        session is null || session.Channel == OrderChannel.Dine ? r.ServiceChargePercent : 0;

    private static decimal FoodSubtotal(IEnumerable<InvoiceLine> lines) =>
        lines.Where(l => l.ItemCode != InvoiceLine.DeliveryCode).Sum(l => l.UnitPrice * l.Qty);

    public async Task<BillDto> GetBill(string tableCode, decimal? discountOverride = null, CancellationToken ct = default,
        string? method = null, bool canDiscount = true)
    {
        if (!canDiscount) discountOverride = null;
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        var sessionId = await OpenSessionId(tableCode, ct);
        var orders = sessionId is int sid ? await OpenOrders(sid, ct) : new List<Order>();
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Code == tableCode, ct);
        var session = sessionId is int s1 ? await _db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == s1, ct) : null;

        decimal paid = sessionId is int s2
            ? (await _db.Payments.Where(p => p.TableSessionId == s2).Select(p => p.Amount).ToListAsync(ct)).Sum() : 0;
        var rateMethod = paid > 0 ? null : method;

        var lines = await BuildLines(orders, r, session, rateMethod, ct);
        // The cashier can override the discount on screen; otherwise use the settings default.
        // A discount comes off the food only, never the delivery charge.
        var discount = discountOverride is decimal d ? Math.Clamp(d, 0, FoodSubtotal(lines)) : SettingsDiscount(r, FoodSubtotal(lines));
        var service = ServicePercent(r, session);
        var m = Invoice.Price(lines, ChargesFor(r, session, rateMethod), discount);
        var shownRate = m.TaxAmount > 0 ? Invoice.DisplayTaxRate(lines) : DefaultTaxRate(r, rateMethod);

        // Parts are always booked at the standard rate, so a split is worked out on that total.
        var splitTotal = m.Total;
        if (rateMethod is not null && DefaultTaxRate(r, rateMethod) != DefaultTaxRate(r, null))
        {
            var standard = await BuildLines(orders, r, session, null, ct);
            splitTotal = Invoice.Price(standard, ChargesFor(r, session, null), discount).Total;
        }

        return new BillDto(r.Name, r.Currency, tableCode, table?.Name ?? tableCode,
            lines.Select(l => new BillLineDto(l.Name, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
            m.Subtotal, service, m.ServiceChargeAmount, shownRate, m.TaxAmount, m.Total, orders.Count > 0,
            paid, Math.Max(0, m.Total - paid),
            r.TaxEnabled, r.TaxName, r.DiscountEnabled, m.DiscountAmount, r.ReceiptEnabled,
            r.LogoUrl, r.ReceiptHeader, r.ReceiptFooter,
            (session?.Channel ?? OrderChannel.Dine).ToString(), session?.CustomerName ?? "", session?.CustomerPhone ?? "",
            session?.DeliveryAddress ?? "", session?.RiderName ?? "",
            m.FurtherTaxAmount, Math.Max(0, splitTotal - paid), m.ServiceTaxAmount, m.RoundingAmount);
    }

    /// <summary>
    /// Refuses a bill that is now worth less than what was already paid on it (the discount was raised after
    /// a part came in). Taking it would count money in the drawer that the bill never charged.
    /// </summary>
    private static void RefuseOverpaid(decimal paid, decimal total)
    {
        if (paid > total + 0.01m)
            throw new DomainException($"Rs {paid:N2} is already paid, which is more than this bill now comes to (Rs {total:N2}). Lower the discount.");
    }

    /// <summary>
    /// What every open table, parcel and delivery comes to right now, priced exactly as its bill opens (the
    /// standard rate and the owner's standing discount, with tax, service and the delivery charge), and what is
    /// still left after any parts paid. The floor tiles and the rider's "collect" amount use this, so they never
    /// show the food alone.
    /// </summary>
    public async Task<Dictionary<string, OpenBillTotal>> OpenBillTotals(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        var sessions = await _db.Sessions.AsNoTracking().Where(s => s.IsOpen).ToListAsync(ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var orders = ids.Count == 0 ? new List<Order>() : await _db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Modifiers)
            .Where(o => ids.Contains(o.TableSessionId) && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        var paid = ids.Count == 0 ? new List<Payment>() : await _db.Payments.AsNoTracking().Where(p => ids.Contains(p.TableSessionId)).ToListAsync(ct);

        var totals = new Dictionary<string, OpenBillTotal>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sessions)
        {
            var mine = orders.Where(o => o.TableSessionId == s.Id).ToList();
            if (mine.Count == 0) continue;
            var lines = await BuildLines(mine, r, s, null, ct);
            var total = Invoice.Price(lines, ChargesFor(r, s, null), SettingsDiscount(r, FoodSubtotal(lines))).Total;
            var p = paid.Where(x => x.TableSessionId == s.Id).Sum(x => x.Amount);
            totals[s.TableCode] = new OpenBillTotal(total, Math.Max(0, total - p));
        }
        return totals;
    }

    /// <summary>
    /// What a seating's open orders come to at the standard rate with the owner's standing discount: the total
    /// that part payments are booked against. Used to refuse a cancel that would leave less to pay than is paid.
    /// </summary>
    public async Task<decimal> StandardTotal(int sessionId, IReadOnlyCollection<Order> openOrders, CancellationToken ct = default)
    {
        if (openOrders.Count == 0) return 0;
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        var session = await _db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var lines = await BuildLines(openOrders, r, session, null, ct);
        return Invoice.Price(lines, ChargesFor(r, session, null), SettingsDiscount(r, FoodSubtotal(lines))).Total;
    }

    /// <summary>
    /// Gives money back from a table's part payments (a dish came off after the guest paid towards it). The newest
    /// parts are reduced first, each in its own method, so the cashier knows how to hand it back. It can't take
    /// back more than was paid, and it is recorded with who did it and why.
    /// </summary>
    public async Task<GiveBackResult> GiveBack(string tableCode, GiveBackRequest req, string by, CancellationToken ct = default)
    {
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length == 0) throw new DomainException("Say why this money is being given back.");
        if (req.Amount <= 0) throw new DomainException("Enter an amount greater than zero.");
        Admin.BillingRules.EnsureAmount(req.Amount);
        var sessionId = await OpenSessionId(tableCode, ct) ?? throw new DomainException("No open bill for this table.");
        var session = await _db.Sessions.FirstAsync(s => s.Id == sessionId, ct);
        var parts = await _db.Payments.Where(p => p.TableSessionId == sessionId).OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).ToListAsync(ct);
        var paid = parts.Sum(p => p.Amount);
        if (req.Amount > paid + 0.001m)
            throw new DomainException($"Only Rs {paid:N2} has been paid on this table, so no more than that can be given back.");

        var left = Math.Round(req.Amount, 2, MidpointRounding.AwayFromZero);
        var back = new List<GiveBackPart>();
        foreach (var p in parts)
        {
            if (left <= 0) break;
            var take = Math.Min(p.Amount, left);
            p.Amount -= take;
            left -= take;
            back.Add(new GiveBackPart(p.Method, take));
            if (p.Amount <= 0) _db.Payments.Remove(p);
        }
        foreach (var g in back.GroupBy(b => b.Method))
            _db.PaymentReturns.Add(new Domain.Ordering.PaymentReturn
            {
                BranchId = session.BranchId, TableSessionId = session.Id, TableName = session.TableName,
                Amount = g.Sum(x => x.Amount), Method = g.Key, Reason = reason, GivenBy = by,
            });
        await _db.SaveChangesAsync(ct);
        var merged = back.GroupBy(b => b.Method).Select(g => new GiveBackPart(g.Key, g.Sum(x => x.Amount))).ToList();
        return new GiveBackResult(merged, paid - req.Amount);
    }

    /// <summary>The discount to pre-apply from the restaurant's settings (0 when off).</summary>
    private static decimal SettingsDiscount(Restaurant r, decimal subtotal)
    {
        if (!r.DiscountEnabled || r.DiscountValue <= 0 || subtotal <= 0) return 0;
        var d = r.DiscountIsPercent ? subtotal * r.DiscountValue / 100m : r.DiscountValue;
        return Math.Round(Math.Clamp(d, 0, subtotal), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Settle the whole table in one go.</summary>
    public async Task<InvoiceDto> Settle(string tableCode, SettleRequest req, string? operatorUsername = null, CancellationToken ct = default,
        bool canDiscount = true)
    {
        Admin.BillingRules.EnsureAmount(req.AmountTendered, "cash received");
        Admin.BillingRules.EnsureAmount(req.Discount, "discount");
        var (r, session, orders) = await LoadOpen(tableCode, ct);
        var paid = (await _db.Payments.Where(p => p.TableSessionId == session.Id).Select(p => p.Amount).ToListAsync(ct)).Sum();
        var rateMethod = paid > 0 ? null : req.Method;
        var lines = await BuildLines(orders, r, session, rateMethod, ct);
        var charges = ChargesFor(r, session, rateMethod);

        // Someone without the discount permission gets the owner's standing discount and no more.
        var discount = canDiscount ? req.Discount : SettingsDiscount(r, FoodSubtotal(lines));

        Customer? khata = null;
        if (string.Equals(req.Method, KhataMethod, StringComparison.OrdinalIgnoreCase))
            khata = await KhataCustomer(req.CustomerId, Invoice.Price(lines, charges, discount).Total - paid, ct);

        var total = Invoice.Price(lines, charges, discount).Total;
        RefuseOverpaid(paid, total);
        if (req.ExpectedTotal is decimal seen && Math.Abs(seen - (total - paid)) > 0.01m)
            throw new DomainException($"This bill changed while it was open. It is now Rs {total - paid:N2}. Check it and settle again.");

        var method = khata is null ? req.Method : KhataMethod;
        if (khata is null && paid > 0)
            method = await RecordLastPart(session, paid, total, req.Method, ct);

        // The applied discount is what the cashier confirmed on screen (pre-filled from
        // settings, but editable) — negative/oversized values are clamped in Price().
        return await Finalize(r, session, orders, tableCode, lines, charges, discount, method,
            req.AmountTendered, operatorUsername, ct, khata, paid);
    }

    /// <summary>Settling a part-paid bill is its last part: kept with the others (saved with the invoice), so the
    /// receipt and the reports say how the money really came in. Returns the label for the invoice.</summary>
    private async Task<string> RecordLastPart(Domain.Tables.TableSession session, decimal paid, decimal total, string method, CancellationToken ct)
    {
        var used = await _db.Payments.Where(p => p.TableSessionId == session.Id).Select(p => p.Method).ToListAsync(ct);
        var rest = total - paid;
        if (rest > 0.01m)
        {
            _db.Payments.Add(new Payment { BranchId = session.BranchId, TableSessionId = session.Id, Amount = rest, Method = method });
            used.Add(method);
        }
        var distinct = used.Distinct().ToList();
        return distinct.Count > 1 ? "Split (" + string.Join("+", distinct) + ")" : distinct.FirstOrDefault() ?? method;
    }

    /// <summary>Who this bill goes on, refusing it if that would take them past their limit.</summary>
    private async Task<Customer> KhataCustomer(int? customerId, decimal amount, CancellationToken ct)
    {
        if (customerId is not int id) throw new DomainException("Choose whose khata this bill goes on.");
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id && c.IsActive, ct)
            ?? throw new DomainException("That customer no longer exists.");
        if (customer.CreditLimit > 0)
        {
            var entries = await _db.CustomerEntries.AsNoTracking().Where(e => e.CustomerId == id).ToListAsync(ct);
            var owed = customer.OpeningBalance + entries.Sum(e => e.BalanceEffect);
            if (owed + amount > customer.CreditLimit)
                throw new DomainException($"This would take {customer.Name} past their khata limit of Rs {customer.CreditLimit:N0}. They currently owe Rs {owed:N0}.");
        }
        return customer;
    }

    private static string TaxLabelFor(Restaurant r) => r.TaxEnabled ? r.TaxName : "";

    /// <summary>
    /// Split-bill: record a partial payment. When the running total covers the bill,
    /// the table is settled automatically and the invoice returned.
    /// </summary>
    public async Task<PayPartResult> PayPartial(string tableCode, PayPartRequest req, string? operatorUsername = null, CancellationToken ct = default,
        bool canDiscount = true)
    {
        if (req.Amount <= 0) throw new DomainException("Enter an amount greater than zero.");
        if (string.Equals(req.Method, KhataMethod, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Khata can't be part of a split bill. Take the part-payment, then settle the rest on khata.");
        var (r, session, orders) = await LoadOpen(tableCode, ct);
        var lines = await BuildLines(orders, r, session, null, ct);
        var discount = canDiscount && req.Discount is decimal asked ? asked : SettingsDiscount(r, FoodSubtotal(lines));
        var charges = ChargesFor(r, session, null);
        var total = Invoice.Price(lines, charges, discount).Total;

        var paidSoFar = (await _db.Payments.Where(p => p.TableSessionId == session.Id).Select(p => p.Amount).ToListAsync(ct)).Sum();
        RefuseOverpaid(paidSoFar, total);
        var left = Math.Round(total - paidSoFar, 2, MidpointRounding.AwayFromZero);
        // A part can't be more than what is left: the extra would count as money in the drawer that no bill
        // charged. Cash with change goes through Settle, which works the change out.
        if (req.Amount > left + 0.01m)
            throw new DomainException($"Only Rs {left:N2} is left to pay on this bill. To take more cash and give change, use Settle.");
        var amount = Math.Min(req.Amount, left);
        _db.Payments.Add(new Payment { BranchId = session.BranchId, TableSessionId = session.Id, Amount = amount, Method = req.Method });
        await _db.SaveChangesAsync(ct);

        var paid = paidSoFar + amount;
        if (paid + 0.01m >= total)
        {
            // Fully covered — settle. Method reflects all the split methods used.
            var methods = await _db.Payments.Where(p => p.TableSessionId == session.Id).Select(p => p.Method).ToListAsync(ct);
            var method = methods.Distinct().Count() > 1 ? "Split (" + string.Join("+", methods.Distinct()) + ")" : methods.FirstOrDefault() ?? req.Method;
            var invoice = await Finalize(r, session, orders, tableCode, lines, charges, discount, method, 0, operatorUsername, ct, alreadyPaid: paid);
            return new PayPartResult(true, paid, 0, invoice);
        }
        return new PayPartResult(false, paid, total - paid, null);
    }

    private async Task<(Restaurant, Domain.Tables.TableSession, List<Order>)> LoadOpen(string tableCode, CancellationToken ct)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        var sessionId = await OpenSessionId(tableCode, ct) ?? throw new DomainException("No open bill for this table.");
        var orders = await OpenOrders(sessionId, ct);
        if (orders.Count == 0) throw new DomainException("Nothing to settle for this table.");
        var session = await _db.Sessions.FirstAsync(s => s.Id == sessionId, ct);
        return (r, session, orders);
    }

    // Creates the invoice, marks orders paid, closes the session, files with FBR, prints.
    private async Task<InvoiceDto> Finalize(Restaurant r, Domain.Tables.TableSession session,
        List<Order> orders, string tableCode, List<InvoiceLine> lines, Invoice.Charges charges, decimal discount, string method,
        decimal amountTendered, string? operatorUsername, CancellationToken ct, Customer? khata = null, decimal alreadyPaid = 0)
    {
        var invoice = Invoice.Create(TenantId, session.Id, tableCode, session.TableName,
            lines, charges, discount, method, 0, "", amountTendered, TaxLabelFor(r), alreadyPaid);
        invoice.BranchId = session.BranchId;
        invoice.CreatedByUsername = operatorUsername ?? "";
        invoice.Channel = session.Channel;
        invoice.DeliveryAddress = session.DeliveryAddress;
        invoice.RiderName = session.RiderName;
        invoice.BuyerName = khata?.Name ?? session.CustomerName;
        invoice.BuyerPhone = khata?.Phone ?? session.CustomerPhone;

        _db.Invoices.Add(invoice);
        foreach (var o in orders) o.Settle();
        session.Close();
        // The table's guests have paid: their passes end, so they can't see or change the next guests' orders.
        var dineTable = session.Channel == OrderChannel.Dine
            ? await _db.Tables.FirstOrDefaultAsync(t => t.Id == session.DiningTableId, ct) : null;
        dineTable?.EndGuestPasses(DateTime.UtcNow);

        // Saved together with the invoice, so a bill can't be settled on khata without reaching the khata.
        CustomerEntry? khataEntry = null;
        if (khata is not null)
        {
            khataEntry = new CustomerEntry
            {
                CustomerId = khata.Id, Type = CustomerEntryType.Sale, Date = BusinessDay.Today,
                Amount = Math.Max(0, invoice.Total - alreadyPaid), CreatedBy = operatorUsername ?? "",
            };
            _db.CustomerEntries.Add(khataEntry);
        }

        // Sequential invoice number = current max + 1. Under two terminals settling at
        // the same instant this can collide on the unique (RestaurantId, Number) index —
        // so we retry with a freshly-read number instead of 500-ing the sale.
        for (int attempt = 1; ; attempt++)
        {
            var nextNumber = (await _db.Invoices.MaxAsync(i => (int?)i.Number, ct) ?? 0) + 1;
            invoice.Number = nextNumber;
            invoice.Usin = $"USIN{nextNumber:D6}";       // unique per restaurant; FBR idempotency key
            try { await _db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException ex) when (attempt < 6 && DbErrors.IsUniqueViolation(ex))
            {
                await Task.Delay(20 * attempt, ct);      // brief backoff, then recompute
            }
        }
        if (khataEntry is not null)
        {
            khataEntry.InvoiceId = invoice.Id;
            khataEntry.Reference = invoice.FormattedNumber;
            khataEntry.Note = invoice.TableName;
        }

        // The bill is saved. Filing it can take seconds and only touches this invoice, so the rest
        // of the restaurant doesn't need to wait for it.
        _turn?.Finish();

        // File with FBR (no-op if fiscal is off). Never blocks the sale — a failure is
        // captured on the invoice and shows in the owner's pending list for retry.
        await _fiscalSync.SyncOnSettle(invoice, r, operatorUsername, ct);
        await _db.SaveChangesAsync(ct);

        // Loyalty points for the member linked to this seating, if any. The sale is already saved,
        // and a loyalty problem must never stop a bill being settled — so it is logged, not thrown.
        try { await _loyalty.AwardForSettledSession(session.Id, invoice.Id, invoice.Total, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Loyalty points were not added for session {SessionId} (invoice {InvoiceId}).", session.Id, invoice.Id);
        }

        foreach (var o in orders)
            await _notifier.OrderChanged(Slug, OrderDto.From(o), ct);
        if (dineTable is not null) await _notifier.TablePassesEnded(Slug, dineTable.Code, ct);

        var dto = InvoiceDto.From(invoice, r.Name, r.Currency, r.LogoUrl, r.ReceiptHeader, r.ReceiptFooter, r.ReceiptEnabled,
            r.FbrEnabled ? r.Ntn : "", r.FbrEnabled ? r.Strn : "");
        // Only auto-print/dispatch a receipt when the owner has receipts turned on.
        if (r.ReceiptEnabled)
            await _print.Dispatch(Slug, PrintJobs.Receipt(dto), ct);
        return dto;
    }

    private const int PktOffsetHours = 5;

    /// <summary>The Reception dashboard: today's takings, open (pending) tables, lifetime
    /// totals and a paginated, searchable history of settled bills.</summary>
    public async Task<ReceptionDashboardDto> ReceptionDashboard(string? q, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);

        var todayStartUtc = DateTime.UtcNow.AddHours(PktOffsetHours).Date.AddHours(-PktOffsetHours);
        var todaySales = await _db.Invoices.AsNoTracking().Where(i => i.IssuedAt >= todayStartUtc).SumAsync(i => (decimal?)i.Total, ct) ?? 0;
        var todayBills = await _db.Invoices.AsNoTracking().CountAsync(i => i.IssuedAt >= todayStartUtc, ct);
        var totalSales = await _db.Invoices.AsNoTracking().SumAsync(i => (decimal?)i.Total, ct) ?? 0;
        var totalBills = await _db.Invoices.AsNoTracking().CountAsync(ct);

        // Pending = open sessions that still have unsettled orders (a running bill).
        // Load every open session's orders in ONE query (not one query per session),
        // then group in memory — avoids an N+1 that grew with the number of open tables.
        var openSessions = await _db.Sessions.AsNoTracking().Where(s => s.IsOpen).ToListAsync(ct);
        var openSessionIds = openSessions.Select(s => s.Id).ToList();
        var openOrders = await _db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => openSessionIds.Contains(o.TableSessionId)
                && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        var bySession = openOrders.GroupBy(o => o.TableSessionId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var pending = openSessions
            .Where(s => bySession.ContainsKey(s.Id))
            .Select(s =>
            {
                var open = bySession[s.Id];
                var amount = open.SelectMany(o => o.Items).Sum(i => i.Price * i.Qty);
                return new OpenTableDto(s.TableCode, s.TableName, open.Count, amount, s.OpenedAt);
            })
            .OrderBy(p => p.OpenedAt).ToList();

        // Settled-bills history (searchable, newest first).
        var hq = _db.Invoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            int? num = int.TryParse(digits, out var n) ? n : null;
            hq = hq.Where(i => i.TableName.Contains(term) || i.PaymentMethod.Contains(term)
                || i.CreatedByUsername.Contains(term) || (num != null && i.Number == num));
        }
        var historyTotal = await hq.CountAsync(ct);
        var invoices = await hq.OrderByDescending(i => i.Number)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var nameOf = await StaffNames.Lookup(_db, ct);
        var history = invoices.Select(i => new SettledBillDto(i.Id, i.FormattedNumber, i.IssuedAt, i.TableName,
            i.PaymentMethod, i.Total, i.AmountTendered, i.ChangeGiven, nameOf(i.CreatedByUsername))).ToList();
        var totalPages = (int)Math.Ceiling(historyTotal / (double)pageSize);

        return new ReceptionDashboardDto(r.Currency, todaySales, todayBills, totalSales, totalBills,
            pending, history, page, pageSize, historyTotal, totalPages);
    }

    public async Task<InvoiceDto?> GetInvoice(int id, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        return inv is null ? null : InvoiceDto.From(inv, r.Name, r.Currency, r.LogoUrl, r.ReceiptHeader, r.ReceiptFooter, r.ReceiptEnabled,
            inv.FiscalStatus != FiscalStatus.NotRequired ? r.Ntn : "", inv.FiscalStatus != FiscalStatus.NotRequired ? r.Strn : "");
    }
}
