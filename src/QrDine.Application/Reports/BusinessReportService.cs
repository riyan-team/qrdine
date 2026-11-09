using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Domain.Inventory;
using QrDine.Domain.Ordering;
using QrDine.Domain.Purchasing;

namespace QrDine.Application.Reports;

public record ReportSummary(decimal Sales, int Bills, decimal AverageBill, decimal Discounts, decimal Tax,
    decimal ServiceCharge, decimal SalesBeforeTax, decimal Expenses, decimal Purchases, decimal SupplierPayments,
    decimal EstimatedProfit, int CancelledOrders, decimal CancelledValue, int ItemsSold);
public record ReportDay(string Date, int Bills, decimal Sales, decimal Tax, decimal Discounts, decimal Expenses, decimal Purchases);
public record ReportRow(string Name, int Count, decimal Amount);
public record ReportHour(int Hour, string Label, int Bills, decimal Sales);
public record ReportCancelled(string When, string Table, string Items, decimal Value, string Reason, string Source);
public record ReportDiscount(string Number, string When, string Table, string Cashier, decimal Before, decimal Discount, decimal Total);
public record ReportClosing(decimal CashSales, decimal OtherSales, decimal CashExpenses, decimal CashSupplierPayments,
    decimal CashInHand, IReadOnlyList<ReportRow> Methods, decimal KhataSales = 0, decimal KhataReceipts = 0, decimal CashKhataReceipts = 0);
public record ReportVoid(string When, string Table, string Item, int Qty, decimal Value, string Reason, string By);
public record ReportCustomer(int Id, string Name, string Phone, decimal Charged, decimal Paid, decimal Balance);
public record ReportSupplier(int Id, string Name, string Phone, decimal Bought, decimal Paid, decimal Balance);
public record ReportStock(string Name, string Unit, decimal Quantity, decimal LowAt, decimal LastCost, decimal Value, bool IsLow);
public record ReportMovement(string When, string Item, string Unit, decimal Change, string Type, string Reason);
public record ReportRating(int Count, double Average, IReadOnlyList<int> Stars, IReadOnlyList<ReportComment> Comments);
public record ReportComment(string When, string Table, int Rating, string Comment);

public record BusinessReport(string Currency, string From, string To, string? Branch,
    ReportSummary Summary, IReadOnlyList<ReportDay> Daily, IReadOnlyList<ReportRow> Items, IReadOnlyList<ReportRow> Categories,
    IReadOnlyList<ReportHour> Hours, IReadOnlyList<ReportRow> Payments, IReadOnlyList<ReportRow> Cashiers,
    IReadOnlyList<ReportRow> Waiters, IReadOnlyList<ReportRow> Tables, IReadOnlyList<ReportRow> Branches,
    IReadOnlyList<ReportRow> Sources, IReadOnlyList<ReportCancelled> Cancelled, IReadOnlyList<ReportDiscount> Discounts,
    ReportClosing Closing, IReadOnlyList<ReportRow> ExpensesByCategory, IReadOnlyList<ReportSupplier> Suppliers,
    decimal SupplierBalance, IReadOnlyList<ReportStock> Stock, decimal StockValue, IReadOnlyList<ReportMovement> Movements,
    ReportRating Ratings, IReadOnlyList<ReportRow> Channels, IReadOnlyList<ReportRow> Riders, IReadOnlyList<ReportVoid> Voids,
    IReadOnlyList<ReportCustomer> Customers, decimal CustomerBalance, decimal DeliveryCharges);

/// <summary>
/// Everything the owner reads about a date range, worked out from the same settled invoices,
/// orders, expenses and khata entries the rest of the app writes. It only ever reads.
/// Figures are added up here rather than in SQL because SQLite can't sum decimals.
/// </summary>
public class BusinessReportService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public BusinessReportService(IAppDbContext db, ICurrentTenant tenant) { _db = db; _tenant = tenant; }

    public async Task<BusinessReport> Build(string? from, string? to, int? branchId, CancellationToken ct = default)
    {
        var end = BusinessDay.Parse(to);
        var start = string.IsNullOrWhiteSpace(from) ? end : BusinessDay.Parse(from);
        if (start > end) (start, end) = (end, start);
        if ((end - start).TotalDays > 366) start = end.AddDays(-366);
        var fromUtc = BusinessDay.StartUtc(start);
        var toUtc = BusinessDay.StartUtc(end.AddDays(1));

        var restaurant = await _db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == _tenant.RestaurantId, ct);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var invoices = await _db.Invoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.IssuedAt >= fromUtc && i.IssuedAt < toUtc && (branchId == null || i.BranchId == branchId))
            .ToListAsync(ct);
        var orders = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc && (branchId == null || o.BranchId == branchId))
            .ToListAsync(ct);
        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => e.Date >= start && e.Date <= end && (branchId == null || e.BranchId == null || e.BranchId == branchId))
            .ToListAsync(ct);
        var allEntries = await _db.SupplierEntries.AsNoTracking().ToListAsync(ct);
        var entries = allEntries.Where(e => e.Date >= start && e.Date <= end).ToList();

        DateTime DayOf(Invoice i) => BusinessDay.ToPkt(i.IssuedAt).Date;
        decimal TaxOf(Invoice i) => i.TaxAmount + i.FurtherTaxAmount;

        // ---- summary ----
        var sales = invoices.Sum(i => i.Total);
        var tax = invoices.Sum(TaxOf);
        var beforeTax = sales - tax;
        var spent = expenses.Sum(e => e.Amount);
        var purchases = entries.Where(e => e.Type == SupplierEntryType.Purchase).Sum(e => e.Amount)
                      - entries.Where(e => e.Type == SupplierEntryType.Return).Sum(e => e.Amount);
        var supplierPayments = entries.Where(e => e.Type == SupplierEntryType.Payment).Sum(e => e.Amount);
        var cancelled = orders.Where(o => o.Status == OrderStatus.Cancelled).ToList();
        decimal ValueOf(Order o) => o.Items.Sum(i => i.LineTotal);

        var summary = new ReportSummary(sales, invoices.Count,
            invoices.Count > 0 ? Math.Round(sales / invoices.Count, 0) : 0,
            invoices.Sum(i => i.DiscountAmount), tax, invoices.Sum(i => i.ServiceChargeAmount), beforeTax,
            spent, purchases, supplierPayments, beforeTax - purchases - spent,
            cancelled.Count, cancelled.Sum(ValueOf),
            invoices.SelectMany(i => i.Lines).Where(l => l.ItemCode != InvoiceLine.DeliveryCode).Sum(l => l.Qty));

        // ---- by day ----
        var daily = new List<ReportDay>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var dayInvoices = invoices.Where(i => DayOf(i) == d).ToList();
            var dayEntries = entries.Where(e => e.Date == d).ToList();
            daily.Add(new ReportDay(d.ToString("yyyy-MM-dd"), dayInvoices.Count, dayInvoices.Sum(i => i.Total),
                dayInvoices.Sum(TaxOf), dayInvoices.Sum(i => i.DiscountAmount),
                expenses.Where(e => e.Date == d).Sum(e => e.Amount),
                dayEntries.Where(e => e.Type == SupplierEntryType.Purchase).Sum(e => e.Amount)
                  - dayEntries.Where(e => e.Type == SupplierEntryType.Return).Sum(e => e.Amount)));
        }

        // ---- what sold, using today's menu to say which category a line belongs to ----
        var allLines = invoices.SelectMany(i => i.Lines).ToList();
        var lines = allLines.Where(l => l.ItemCode != InvoiceLine.DeliveryCode).ToList();
        var deliveryCharges = allLines.Where(l => l.ItemCode == InvoiceLine.DeliveryCode).Sum(l => l.LineTotal);
        var items = lines.GroupBy(l => l.Name)
            .Select(g => new ReportRow(g.Key, g.Sum(l => l.Qty), g.Sum(l => l.LineTotal)))
            .OrderByDescending(r => r.Amount).ToList();

        var menu = await _db.MenuItems.AsNoTracking().Select(m => new { m.Id, m.CategoryId }).ToListAsync(ct);
        var categoryNames = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var categoryOfItem = menu.ToDictionary(m => m.Id.ToString(), m => categoryNames.GetValueOrDefault(m.CategoryId, "Other"));
        var categories = lines.GroupBy(l => categoryOfItem.GetValueOrDefault(l.ItemCode, "Other"))
            .Select(g => new ReportRow(g.Key, g.Sum(l => l.Qty), g.Sum(l => l.LineTotal)))
            .OrderByDescending(r => r.Amount).ToList();

        // ---- by hour, busiest first is for the eye: the screen keeps them in clock order ----
        var hours = invoices.GroupBy(i => BusinessDay.ToPkt(i.IssuedAt).Hour).OrderBy(g => g.Key)
            .Select(g => new ReportHour(g.Key, HourLabel(g.Key), g.Count(), g.Sum(i => i.Total))).ToList();

        var payments = invoices.GroupBy(i => i.PaymentMethod)
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(i => i.Total))).OrderByDescending(r => r.Amount).ToList();
        var nameOf = await StaffNames.Lookup(_db, ct);
        var cashiers = invoices.GroupBy(i => string.IsNullOrWhiteSpace(i.CreatedByUsername) ? "Unknown" : nameOf(i.CreatedByUsername))
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(i => i.Total))).OrderByDescending(r => r.Amount).ToList();
        var tables = invoices.GroupBy(i => i.TableName)
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(i => i.Total))).OrderByDescending(r => r.Amount).ToList();
        var byBranch = invoices.GroupBy(i => branches.GetValueOrDefault(i.BranchId, "Main"))
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(i => i.Total))).OrderByDescending(r => r.Amount).ToList();

        var live = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        var waiters = live.Where(o => !string.IsNullOrWhiteSpace(o.WaiterName)).GroupBy(o => o.WaiterName!)
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(ValueOf))).OrderByDescending(r => r.Amount).ToList();
        var sources = live.GroupBy(o => o.Source.ToString())
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(ValueOf))).OrderByDescending(r => r.Amount).ToList();

        var cancelledRows = cancelled.OrderByDescending(o => o.CreatedAt).Select(o => new ReportCancelled(
            BusinessDay.ToPkt(o.CreatedAt).ToString("yyyy-MM-dd HH:mm"), o.TableName,
            string.Join(", ", o.Items.Select(i => $"{i.Qty}× {i.Name}")), ValueOf(o),
            string.IsNullOrWhiteSpace(o.CancelReason) ? "No reason given" : o.CancelReason!, o.Source.ToString())).ToList();
        var discounts = invoices.Where(i => i.DiscountAmount > 0).OrderByDescending(i => i.IssuedAt).Select(i => new ReportDiscount(
            i.FormattedNumber, BusinessDay.ToPkt(i.IssuedAt).ToString("yyyy-MM-dd HH:mm"), i.TableName, nameOf(i.CreatedByUsername),
            i.Total + i.DiscountAmount, i.DiscountAmount, i.Total)).ToList();

        // ---- closing: split bills count by the money actually taken in each method ----
        // A bill may have had part-payments before the rest was settled, so each part counts by its own method.
        var sessionIds = invoices.Select(i => i.TableSessionId).ToHashSet();
        var parts = sessionIds.Count == 0 ? new List<Payment>()
            : await _db.Payments.AsNoTracking().Where(p => sessionIds.Contains(p.TableSessionId)).ToListAsync(ct);
        var taken = new List<(string Method, decimal Total)>();
        foreach (var i in invoices)
        {
            var mine = parts.Where(p => p.TableSessionId == i.TableSessionId).ToList();
            taken.AddRange(mine.Select(p => (p.Method, p.Amount)));
            var rest = i.Total - mine.Sum(p => p.Amount);
            if (rest > 0.01m) taken.Add((i.PaymentMethod.StartsWith("Split") ? "Other" : i.PaymentMethod, rest));
        }
        var methods = taken.GroupBy(t => t.Method)
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(t => t.Total))).OrderByDescending(r => r.Amount).ToList();
        var cashSales = taken.Where(t => IsCash(t.Method)).Sum(t => t.Total);
        var cashExpenses = expenses.Where(e => IsCash(e.PaymentMethod)).Sum(e => e.Amount);
        var cashSupplier = entries.Where(e => e.Type == SupplierEntryType.Payment && IsCash(e.PaymentMethod)).Sum(e => e.Amount);

        // ---- customer khata: what went on credit, and what came back ----
        var allKhata = await _db.CustomerEntries.AsNoTracking().ToListAsync(ct);
        var khataInRange = allKhata.Where(e => e.Date >= start && e.Date <= end).ToList();
        var khataSales = taken.Where(t => IsKhata(t.Method)).Sum(t => t.Total);
        var khataReceipts = khataInRange.Where(e => e.Type == Domain.Customers.CustomerEntryType.Payment).Sum(e => e.Amount);
        var cashKhata = khataInRange.Where(e => e.Type == Domain.Customers.CustomerEntryType.Payment && IsCash(e.PaymentMethod)).Sum(e => e.Amount);

        var closing = new ReportClosing(cashSales, taken.Sum(t => t.Total) - cashSales - khataSales, cashExpenses, cashSupplier,
            cashSales + cashKhata - cashExpenses - cashSupplier, methods, khataSales, khataReceipts, cashKhata);

        var customers = await _db.Customers.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
        var customerRows = customers.Select(c =>
        {
            var mine = allKhata.Where(e => e.CustomerId == c.Id).ToList();
            var inRange = mine.Where(e => e.Date >= start && e.Date <= end).ToList();
            return new ReportCustomer(c.Id, c.Name, c.Phone,
                inRange.Where(e => e.Type == Domain.Customers.CustomerEntryType.Sale).Sum(e => e.Amount),
                inRange.Where(e => e.Type == Domain.Customers.CustomerEntryType.Payment).Sum(e => e.Amount),
                c.OpeningBalance + mine.Sum(e => e.BalanceEffect));
        }).Where(c => c.Balance != 0 || c.Charged != 0 || c.Paid != 0).OrderByDescending(c => c.Balance).ToList();

        // ---- how the food left the building, and who took it ----
        var channels = invoices.GroupBy(i => ChannelLabel(i.Channel))
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(i => i.Total))).OrderByDescending(r => r.Amount).ToList();
        var riders = invoices.Where(i => i.Channel == OrderChannel.Delivery)
            .GroupBy(i => string.IsNullOrWhiteSpace(i.RiderName) ? "No rider set" : i.RiderName)
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(i => i.Total))).OrderByDescending(r => r.Amount).ToList();

        var voids = await _db.OrderVoids.AsNoTracking()
            .Where(v => v.VoidedAt >= fromUtc && v.VoidedAt < toUtc && (branchId == null || v.BranchId == branchId))
            .OrderByDescending(v => v.VoidedAt).Take(500).ToListAsync(ct);
        var voidRows = voids.Select(v => new ReportVoid(BusinessDay.ToPkt(v.VoidedAt).ToString("yyyy-MM-dd HH:mm"), v.TableName,
            v.ItemName, v.Qty, v.UnitPrice * v.Qty, v.Reason, nameOf(v.VoidedBy))).ToList();

        var expensesByCategory = expenses.GroupBy(e => e.Category)
            .Select(g => new ReportRow(g.Key, g.Count(), g.Sum(e => e.Amount))).OrderByDescending(r => r.Amount).ToList();

        // ---- what's owed is as of now, not as of the range ----
        var suppliers = await _db.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);
        var supplierRows = suppliers.Select(s =>
        {
            var mine = allEntries.Where(e => e.SupplierId == s.Id).ToList();
            var inRange = mine.Where(e => e.Date >= start && e.Date <= end).ToList();
            return new ReportSupplier(s.Id, s.Name, s.Phone,
                inRange.Where(e => e.Type == SupplierEntryType.Purchase).Sum(e => e.Amount),
                inRange.Where(e => e.Type == SupplierEntryType.Payment).Sum(e => e.Amount),
                s.OpeningBalance + mine.Sum(e => e.BalanceEffect));
        }).OrderByDescending(s => s.Balance).ToList();

        // ---- stock, also as of now ----
        var stock = await _db.Stock.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var stockRows = stock.Select(s => new ReportStock(s.Name, s.Unit, s.Quantity, s.LowThreshold, s.LastCost,
            Math.Round(Math.Max(0, s.Quantity) * s.LastCost, 0), s.IsLow)).ToList();
        var stockById = stock.ToDictionary(s => s.Id);
        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.CreatedAt >= fromUtc && m.CreatedAt < toUtc).OrderByDescending(m => m.CreatedAt).Take(500).ToListAsync(ct);
        var movementRows = movements.Select(m => new ReportMovement(
            BusinessDay.ToPkt(m.CreatedAt).ToString("yyyy-MM-dd HH:mm"),
            stockById.TryGetValue(m.StockItemId, out var it) ? it.Name : "(deleted)",
            stockById.TryGetValue(m.StockItemId, out var it2) ? it2.Unit : "",
            m.Delta, m.Type.ToString(), m.Reason ?? "")).ToList();

        // ---- what guests said ----
        var feedback = await _db.Feedbacks.AsNoTracking()
            .Where(f => f.CreatedAt >= fromUtc && f.CreatedAt < toUtc).OrderByDescending(f => f.CreatedAt).ToListAsync(ct);
        var ratings = new ReportRating(feedback.Count,
            feedback.Count > 0 ? Math.Round(feedback.Average(f => (double)f.Rating), 1) : 0,
            Enumerable.Range(1, 5).Select(star => feedback.Count(f => f.Rating == star)).ToList(),
            feedback.Where(f => !string.IsNullOrWhiteSpace(f.Comment)).Take(50).Select(f => new ReportComment(
                BusinessDay.ToPkt(f.CreatedAt).ToString("yyyy-MM-dd HH:mm"), f.TableCode, f.Rating, f.Comment!)).ToList());

        return new BusinessReport(restaurant.Currency, start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"),
            branchId != null ? branches.GetValueOrDefault(branchId.Value) : null,
            summary, daily, items, categories, hours, payments, cashiers, waiters, tables, byBranch, sources,
            cancelledRows, discounts, closing, expensesByCategory, supplierRows, supplierRows.Sum(s => s.Balance),
            stockRows, stockRows.Sum(s => s.Value), movementRows, ratings,
            channels, riders, voidRows, customerRows, customerRows.Sum(c => c.Balance), deliveryCharges);
    }

    /// <summary>The dishes guests order most, for the "most ordered" row on the menu. Only ids, no money.</summary>
    public async Task<IReadOnlyList<int>> PopularItems(int take = 8, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var counts = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items)
            .GroupBy(i => i.MenuItemId)
            .Select(g => new { Id = g.Key, Qty = g.Sum(i => i.Qty) })
            .OrderByDescending(x => x.Qty).Take(take).ToListAsync(ct);
        return counts.Select(x => x.Id).ToList();
    }

    private static bool IsCash(string? method) => string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase);
    private static bool IsKhata(string? method) => string.Equals(method, "Khata", StringComparison.OrdinalIgnoreCase);

    private static string ChannelLabel(OrderChannel channel) => channel switch
    {
        OrderChannel.Takeaway => "Parcel",
        OrderChannel.Delivery => "Delivery",
        _ => "Dine-in",
    };

    private static string HourLabel(int hour) => hour switch
    {
        0 => "12 am",
        12 => "12 pm",
        < 12 => $"{hour} am",
        _ => $"{hour - 12} pm",
    };
}
