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
    string FiscalStatus, string FiscalNumber);

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

    /// <summary>Flatten settled invoices in a date window for a CSV export / tax return.</summary>
    public async Task<IReadOnlyList<InvoiceReportRow>> ExportInvoices(DateTime fromUtc, DateTime toUtc, int? branchId, CancellationToken ct = default)
    {
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.IssuedAt >= fromUtc && i.IssuedAt <= toUtc && (branchId == null || i.BranchId == branchId))
            .OrderBy(i => i.Number).ToListAsync(ct);
        return invoices.Select(i => new InvoiceReportRow(
            i.Number, i.IssuedAt, branches.GetValueOrDefault(i.BranchId, ""), i.TableName, i.PaymentMethod,
            i.Subtotal, i.ServiceChargeAmount, i.TaxAmount, i.FurtherTaxAmount, i.DiscountAmount, i.Total,
            i.FiscalStatus.ToString(), i.FiscalNumber ?? "")).ToList();
    }
}
