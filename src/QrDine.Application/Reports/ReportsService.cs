using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Billing;

namespace QrDine.Application.Reports;

public record DashboardDto(
    string Currency,
    decimal TodaySales, int TodayBills,
    decimal PeriodSales, int PeriodBills, decimal PeriodAvgBill, decimal PeriodTax, decimal PeriodFurtherTax,
    decimal PeriodNetSales,
    IReadOnlyList<DailyPointDto> Trend,
    IReadOnlyList<PayBreakdownDto> Payments,
    IReadOnlyList<TopItemDto> TopItems,
    int LowStock, IReadOnlyList<string> LowStockItems,
    IReadOnlyList<BranchOptionDto> Branches);

public record BranchOptionDto(int Id, string Name);

public record DailyPointDto(string Date, decimal Total, int Bills);
public record PayBreakdownDto(string Method, int Count, decimal Amount);
public record TopItemDto(string Name, int Qty, decimal Revenue);

/// <summary>One settled invoice, flattened for a CSV export / tax return.</summary>
public record InvoiceReportRow(int Number, DateTime IssuedAt, string Branch, string Table, string PaymentMethod,
    decimal NetSales, decimal ServiceCharge, decimal SalesTax, decimal FurtherTax, decimal Discount, decimal Total,
    decimal AmountTendered, decimal ChangeGiven, string Cashier, string FiscalStatus, string FiscalNumber);

/// <summary>One row in the paginated sales report (the same data the dashboard shows).</summary>
public record SalesRow(int Id, string Number, DateTime IssuedAt, string Branch, string Table,
    string PaymentMethod, decimal Subtotal, decimal Discount, decimal Tax, decimal ServiceCharge, decimal Total,
    decimal AmountTendered, decimal ChangeGiven, string Cashier, string FiscalStatus);

/// <summary>A page of sales rows plus the totals across the whole filtered set.</summary>
public record SalesPage(IReadOnlyList<SalesRow> Rows, int Page, int PageSize, int Total, int TotalPages,
    string Currency, decimal SumSales, decimal SumTax, decimal SumDiscount, decimal SumNet);

/// <summary>
/// Owner sales analytics, built from settled invoices. Days are bucketed in
/// Pakistan time (UTC+5) so "today" matches the till, not the server clock.
/// </summary>
public class ReportsService
{
    private const int PktOffsetHours = 5;
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public ReportsService(IAppDbContext db, ICurrentTenant tenant) { _db = db; _tenant = tenant; }

    public async Task<DashboardDto> GetDashboard(int days = 7, int? branchId = null, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 31);
        var r = await _db.Restaurants.FirstAsync(x => x.Id == _tenant.RestaurantId, ct);

        var todayPkt = DateTime.UtcNow.AddHours(PktOffsetHours).Date;
        var startPkt = todayPkt.AddDays(-(days - 1));
        var startUtc = startPkt.AddHours(-PktOffsetHours);

        var invoices = await _db.Invoices.Include(i => i.Lines)
            .Where(i => i.IssuedAt >= startUtc && (branchId == null || i.BranchId == branchId))
            .ToListAsync(ct);

        DateTime Day(Invoice i) => i.IssuedAt.AddHours(PktOffsetHours).Date;
        var today = invoices.Where(i => Day(i) == todayPkt).ToList();

        var trend = Enumerable.Range(0, days).Select(k =>
        {
            var d = startPkt.AddDays(k);
            var inv = invoices.Where(i => Day(i) == d).ToList();
            return new DailyPointDto(d.ToString("dd MMM"), inv.Sum(x => x.Total), inv.Count);
        }).ToList();

        var payments = invoices
            .GroupBy(i => i.PaymentMethod)
            .Select(g => new PayBreakdownDto(g.Key, g.Count(), g.Sum(x => x.Total)))
            .OrderByDescending(p => p.Amount).ToList();

        var topItems = invoices.SelectMany(i => i.Lines)
            .GroupBy(l => l.Name)
            .Select(g => new TopItemDto(g.Key, g.Sum(l => l.Qty), g.Sum(l => l.LineTotal)))
            .OrderByDescending(t => t.Revenue).Take(6).ToList();

        var periodSales = invoices.Sum(i => i.Total);
        var periodBills = invoices.Count;
        var avg = periodBills > 0 ? Math.Round(periodSales / periodBills, 0) : 0;

        var lowStock = await _db.Stock.AsNoTracking().Where(s => s.Quantity <= s.LowThreshold)
            .OrderBy(s => s.Name).Select(s => s.Name).ToListAsync(ct);

        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Id)
            .Select(b => new BranchOptionDto(b.Id, b.Name)).ToListAsync(ct);

        return new DashboardDto(r.Currency,
            today.Sum(i => i.Total), today.Count,
            periodSales, periodBills, avg,
            invoices.Sum(i => i.TaxAmount), invoices.Sum(i => i.FurtherTaxAmount), invoices.Sum(i => i.Subtotal),
            trend, payments, topItems, lowStock.Count, lowStock, branches);
    }

    // Shared filter for both the paginated report and the CSV export, so the download
    // always matches exactly what the screen shows.
    private IQueryable<Domain.Billing.Invoice> FilterInvoices(DateTime fromUtc, DateTime toUtc, int? branchId, string? q, string? method)
    {
        var query = _db.Invoices.AsNoTracking().Where(i => i.IssuedAt >= fromUtc && i.IssuedAt <= toUtc);
        if (branchId != null) query = query.Where(i => i.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(method)) query = query.Where(i => i.PaymentMethod == method);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            int? num = int.TryParse(digits, out var n) ? n : null;
            query = query.Where(i =>
                i.TableName.Contains(term) || i.PaymentMethod.Contains(term) ||
                i.CreatedByUsername.Contains(term) || (num != null && i.Number == num));
        }
        return query;
    }

    /// <summary>A page of settled invoices with filters, search and server-side paging.</summary>
    public async Task<SalesPage> GetSales(DateTime fromUtc, DateTime toUtc, int? branchId, string? q, string? method,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == _tenant.RestaurantId, ct);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var query = FilterInvoices(fromUtc, toUtc, branchId, q, method);
        var total = await query.CountAsync(ct);
        var sumSales = await query.SumAsync(i => (decimal?)i.Total, ct) ?? 0;
        var sumTax = await query.SumAsync(i => (decimal?)(i.TaxAmount + i.FurtherTaxAmount), ct) ?? 0;
        var sumDiscount = await query.SumAsync(i => (decimal?)i.DiscountAmount, ct) ?? 0;
        var sumNet = await query.SumAsync(i => (decimal?)i.Subtotal, ct) ?? 0;

        var invoices = await query.OrderByDescending(i => i.Number)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var nameOf = await StaffNames.Lookup(_db, ct);
        var rows = invoices.Select(i => new SalesRow(
            i.Id, i.FormattedNumber, i.IssuedAt, branches.GetValueOrDefault(i.BranchId, ""), i.TableName,
            i.PaymentMethod, i.Subtotal, i.DiscountAmount, i.TaxAmount + i.FurtherTaxAmount, i.ServiceChargeAmount,
            i.Total, i.AmountTendered, i.ChangeGiven, nameOf(i.CreatedByUsername), i.FiscalStatus.ToString())).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        return new SalesPage(rows, page, pageSize, total, totalPages, r.Currency, sumSales, sumTax, sumDiscount, sumNet);
    }

    /// <summary>Flatten settled invoices in a date window for a CSV export / tax return.
    /// Uses the same filters as <see cref="GetSales"/> so the download matches the screen.</summary>
    public async Task<IReadOnlyList<InvoiceReportRow>> ExportInvoices(DateTime fromUtc, DateTime toUtc, int? branchId,
        string? q = null, string? method = null, CancellationToken ct = default)
    {
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var invoices = await FilterInvoices(fromUtc, toUtc, branchId, q, method)
            .OrderBy(i => i.Number).ToListAsync(ct);
        var nameOf = await StaffNames.Lookup(_db, ct);
        return invoices.Select(i => new InvoiceReportRow(
            i.Number, i.IssuedAt, branches.GetValueOrDefault(i.BranchId, ""), i.TableName, i.PaymentMethod,
            i.Subtotal, i.ServiceChargeAmount, i.TaxAmount, i.FurtherTaxAmount, i.DiscountAmount, i.Total,
            i.AmountTendered, i.ChangeGiven, nameOf(i.CreatedByUsername),
            i.FiscalStatus.ToString(), i.FiscalNumber ?? "")).ToList();
    }
}
