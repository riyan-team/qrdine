using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Application.Reports;
using QrDine.Domain.Billing;

namespace QrDine.Application.Billing;

public record DayCloseInput(string? Date, decimal OpeningCash, decimal CountedCash, decimal TakenOut, string? Note, int? BranchId);
public record DayClosedRow(decimal OpeningCash, decimal Expected, decimal Counted, decimal Difference, decimal TakenOut,
    string Note, string ClosedBy, DateTime ClosedAt);
public record DayCloseView(string Date, int Bills, decimal Sales, decimal OpeningCash, decimal CashSales, decimal KhataReceipts,
    decimal CashExpenses, decimal SupplierPayments, decimal Expected, decimal KhataSales, decimal OtherSales,
    IReadOnlyList<ReportRow> Methods, DayClosedRow? Closed);

/// <summary>
/// The end-of-day cash count. The figures come from the same report the owner reads, so the count
/// and the report can't disagree.
/// </summary>
public class DayCloseService
{
    private readonly IAppDbContext _db;
    private readonly BusinessReportService _reports;

    public DayCloseService(IAppDbContext db, BusinessReportService reports) { _db = db; _reports = reports; }

    public async Task<DayCloseView> View(string? date, int? branchId, CancellationToken ct = default)
    {
        var day = BusinessDay.Parse(date);
        var key = branchId ?? 0;
        var closed = await _db.DayClosings.AsNoTracking().FirstOrDefaultAsync(c => c.Date == day && c.BranchId == key, ct);
        var opening = closed?.OpeningCash ?? await OpeningFor(day, key, ct);

        var report = await _reports.Build(day.ToString("yyyy-MM-dd"), day.ToString("yyyy-MM-dd"), branchId, ct);
        var c = report.Closing;
        var expected = opening + c.CashSales + c.CashKhataReceipts - c.CashExpenses - c.CashSupplierPayments;
        var nameOf = await StaffNames.Lookup(_db, ct);

        return new DayCloseView(day.ToString("yyyy-MM-dd"), report.Summary.Bills, report.Summary.Sales, opening, c.CashSales,
            c.CashKhataReceipts, c.CashExpenses, c.CashSupplierPayments, expected, c.KhataSales, c.OtherSales, c.Methods,
            closed is null ? null : new DayClosedRow(closed.OpeningCash, closed.Expected, closed.Counted, closed.Difference,
                closed.TakenOut, closed.Note, nameOf(closed.ClosedBy), DateTime.SpecifyKind(closed.ClosedAt, DateTimeKind.Utc)));
    }

    /// <summary>Saves the count. Counting the same day again replaces the earlier count, as it should when a late bill comes in.</summary>
    public async Task<DayCloseView> Close(DayCloseInput input, string closedBy, CancellationToken ct = default)
    {
        var day = BusinessDay.Parse(input.Date);
        var key = input.BranchId ?? 0;
        var report = await _reports.Build(day.ToString("yyyy-MM-dd"), day.ToString("yyyy-MM-dd"), input.BranchId, ct);
        var c = report.Closing;
        var cashIn = c.CashSales + c.CashKhataReceipts;
        var cashOut = c.CashExpenses + c.CashSupplierPayments;
        var expected = input.OpeningCash + cashIn - cashOut;

        var row = await _db.DayClosings.FirstOrDefaultAsync(x => x.Date == day && x.BranchId == key, ct);
        if (row is null)
        {
            row = new DayClosing { Date = day, BranchId = key };
            _db.DayClosings.Add(row);
        }
        row.OpeningCash = input.OpeningCash;
        row.CashIn = cashIn;
        row.CashOut = cashOut;
        row.Expected = expected;
        row.Counted = input.CountedCash;
        row.Difference = input.CountedCash - expected;
        row.TakenOut = Math.Max(0, input.TakenOut);
        row.Note = (input.Note ?? "").Trim();
        row.ClosedBy = closedBy;
        row.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await View(input.Date, input.BranchId, ct);
    }

    /// <summary>Today opens with whatever was left in the drawer after the last count.</summary>
    private async Task<decimal> OpeningFor(DateTime day, int branchKey, CancellationToken ct)
    {
        var last = await _db.DayClosings.AsNoTracking().Where(c => c.BranchId == branchKey && c.Date < day)
            .OrderByDescending(c => c.Date).FirstOrDefaultAsync(ct);
        return last is null ? 0 : last.Counted - last.TakenOut;
    }
}
