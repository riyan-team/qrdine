using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Printing;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
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

    public BillingService(IAppDbContext db, ICurrentTenant tenant, IOrderNotifier notifier,
        FiscalSyncService fiscalSync, IPrintDispatcher print)
    {
        _db = db; _tenant = tenant; _notifier = notifier; _fiscalSync = fiscalSync; _print = print;
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
    private async Task<List<InvoiceLine>> BuildLines(IEnumerable<Order> orders, Restaurant r, CancellationToken ct)
    {
        var items = orders.SelectMany(o => o.Items).ToList();
        var menuIds = items.Select(i => i.MenuItemId).Distinct().ToList();
        var menu = (await _db.MenuItems.AsNoTracking()
                .Where(m => menuIds.Contains(m.Id))
                .Select(m => new { m.Id, m.TaxRate, m.FurtherTaxRate, m.PctCode })
                .ToListAsync(ct))
            .ToDictionary(m => m.Id);

        return items
            .GroupBy(i => new
            {
                i.MenuItemId,
                Name = i.VariantName is null ? i.Name : $"{i.Name} ({i.VariantName})",
                i.Price,
            })
            .Select(g =>
            {
                menu.TryGetValue(g.Key.MenuItemId, out var mi);
                // Tax OFF → no tax at all. Tax ON → the item's own rate if it has one,
                // otherwise the restaurant's default GST rate applies to the item.
                var itemRate = mi?.TaxRate ?? 0;
                var taxRate = !r.TaxEnabled ? 0 : (itemRate > 0 ? itemRate : r.TaxPercent);
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
    }

    public async Task<BillDto> GetBill(string tableCode, decimal? discountOverride = null, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        var sessionId = await OpenSessionId(tableCode, ct);
        var orders = sessionId is int sid ? await OpenOrders(sid, ct) : new List<Order>();
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Code == tableCode, ct);

        var lines = await BuildLines(orders, r, ct);
        var subtotal = lines.Sum(l => l.UnitPrice * l.Qty);
        // The cashier can override the discount on screen; otherwise use the settings default.
        var discount = discountOverride is decimal d ? Math.Clamp(d, 0, subtotal) : SettingsDiscount(r, subtotal);
        var m = Invoice.Price(lines, r.ServiceChargePercent, discount);

        decimal paid = sessionId is int s2 ? await _db.Payments.Where(p => p.TableSessionId == s2).SumAsync(p => (decimal?)p.Amount, ct) ?? 0 : 0;

        return new BillDto(r.Name, r.Currency, tableCode, table?.Name ?? tableCode,
            lines.Select(l => new BillLineDto(l.Name, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
            m.Subtotal, r.ServiceChargePercent, m.ServiceChargeAmount, r.TaxPercent, m.TaxAmount, m.Total, orders.Count > 0,
            paid, Math.Max(0, m.Total - paid),
            r.TaxEnabled, r.TaxName, r.DiscountEnabled, m.DiscountAmount, r.ReceiptEnabled,
            r.LogoUrl, r.ReceiptHeader, r.ReceiptFooter);
    }

    /// <summary>The discount to pre-apply from the restaurant's settings (0 when off).</summary>
    private static decimal SettingsDiscount(Restaurant r, decimal subtotal)
    {
        if (!r.DiscountEnabled || r.DiscountValue <= 0 || subtotal <= 0) return 0;
        var d = r.DiscountIsPercent ? subtotal * r.DiscountValue / 100m : r.DiscountValue;
        return Math.Round(Math.Clamp(d, 0, subtotal), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Settle the whole table in one go.</summary>
    public async Task<InvoiceDto> Settle(string tableCode, SettleRequest req, string? operatorUsername = null, CancellationToken ct = default)
    {
        var (r, session, orders) = await LoadOpen(tableCode, ct);
        var lines = await BuildLines(orders, r, ct);
        // The applied discount is what the cashier confirmed on screen (pre-filled from
        // settings, but editable) — negative/oversized values are clamped in Price().
        return await Finalize(r, session, orders, tableCode, lines, req.Discount, req.Method,
            req.AmountTendered, operatorUsername, ct);
    }

    private static string TaxLabelFor(Restaurant r) => r.TaxEnabled ? r.TaxName : "";

    /// <summary>
    /// Split-bill: record a partial payment. When the running total covers the bill,
    /// the table is settled automatically and the invoice returned.
    /// </summary>
    public async Task<PayPartResult> PayPartial(string tableCode, PayPartRequest req, string? operatorUsername = null, CancellationToken ct = default)
    {
        if (req.Amount <= 0) throw new DomainException("Enter an amount greater than zero.");
        var (r, session, orders) = await LoadOpen(tableCode, ct);
        var lines = await BuildLines(orders, r, ct);
        var total = Invoice.Price(lines, r.ServiceChargePercent, 0).Total;

        var paidSoFar = await _db.Payments.Where(p => p.TableSessionId == session.Id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        _db.Payments.Add(new Payment { BranchId = session.BranchId, TableSessionId = session.Id, Amount = req.Amount, Method = req.Method });
        await _db.SaveChangesAsync(ct);

        var paid = paidSoFar + req.Amount;
        if (paid + 0.01m >= total)
        {
            // Fully covered — settle. Method reflects all the split methods used.
            var methods = await _db.Payments.Where(p => p.TableSessionId == session.Id).Select(p => p.Method).ToListAsync(ct);
            var method = methods.Distinct().Count() > 1 ? "Split (" + string.Join("+", methods.Distinct()) + ")" : methods.FirstOrDefault() ?? req.Method;
            var invoice = await Finalize(r, session, orders, tableCode, lines, 0, method, paid, operatorUsername, ct);
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
        List<Order> orders, string tableCode, List<InvoiceLine> lines, decimal discount, string method,
        decimal amountTendered, string? operatorUsername, CancellationToken ct)
    {
        var invoice = Invoice.Create(TenantId, session.Id, tableCode, session.TableName,
            lines, r.ServiceChargePercent, discount, method, 0, "", amountTendered, TaxLabelFor(r));
        invoice.BranchId = session.BranchId;
        invoice.CreatedByUsername = operatorUsername ?? "";

        _db.Invoices.Add(invoice);
        foreach (var o in orders) o.Settle();
        session.Close();

        // Sequential invoice number = current max + 1. Under two terminals settling at
        // the same instant this can collide on the unique (RestaurantId, Number) index —
        // so we retry with a freshly-read number instead of 500-ing the sale.
        for (int attempt = 1; ; attempt++)
        {
            var nextNumber = (await _db.Invoices.MaxAsync(i => (int?)i.Number, ct) ?? 0) + 1;
            invoice.Number = nextNumber;
            invoice.Usin = $"USIN{nextNumber:D6}";       // unique per restaurant; FBR idempotency key
            try { await _db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException) when (attempt < 6)
            {
                await Task.Delay(20 * attempt, ct);      // brief backoff, then recompute
            }
        }

        // File with FBR (no-op if fiscal is off). Never blocks the sale — a failure is
        // captured on the invoice and shows in the owner's pending list for retry.
        await _fiscalSync.SyncOnSettle(invoice, r, operatorUsername, ct);
        await _db.SaveChangesAsync(ct);

        foreach (var o in orders)
            await _notifier.OrderChanged(Slug, OrderDto.From(o), ct);

        var dto = InvoiceDto.From(invoice, r.Name, r.Currency, r.LogoUrl, r.ReceiptHeader, r.ReceiptFooter, r.ReceiptEnabled);
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
        var openSessions = await _db.Sessions.AsNoTracking().Where(s => s.IsOpen).ToListAsync(ct);
        var pending = new List<OpenTableDto>();
        foreach (var s in openSessions)
        {
            var open = await OpenOrders(s.Id, ct);
            if (open.Count == 0) continue;
            var amount = open.SelectMany(o => o.Items).Sum(i => i.Price * i.Qty);
            pending.Add(new OpenTableDto(s.TableCode, s.TableName, open.Count, amount, s.OpenedAt));
        }
        pending = pending.OrderBy(p => p.OpenedAt).ToList();

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
        var history = invoices.Select(i => new SettledBillDto(i.Id, i.FormattedNumber, i.IssuedAt, i.TableName,
            i.PaymentMethod, i.Total, i.AmountTendered, i.ChangeGiven, i.CreatedByUsername)).ToList();
        var totalPages = (int)Math.Ceiling(historyTotal / (double)pageSize);

        return new ReceptionDashboardDto(r.Currency, todaySales, todayBills, totalSales, totalBills,
            pending, history, page, pageSize, historyTotal, totalPages);
    }

    public async Task<InvoiceDto?> GetInvoice(int id, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        return inv is null ? null : InvoiceDto.From(inv, r.Name, r.Currency, r.LogoUrl, r.ReceiptHeader, r.ReceiptFooter, r.ReceiptEnabled);
    }
}
