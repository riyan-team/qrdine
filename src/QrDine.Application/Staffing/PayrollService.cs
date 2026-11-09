using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Expenses;
using QrDine.Domain.Staffing;

namespace QrDine.Application.Staffing;

public record EmployeeInput(string Name, string? Phone, string? Designation, decimal MonthlySalary, string? JoinedOn, string? Notes);
public record PayrollEntryInput(string Type, decimal Amount, string? Month, string? Date, string? Method, string? Note);
public record AttendanceInput(int EmployeeId, string Date, string? Status);
public record PayrollRow(int Id, string Name, string Phone, string Designation, decimal MonthlySalary, string? JoinedOn, string Notes,
    decimal Advances, decimal Bonuses, decimal Deductions, decimal Paid, decimal Due,
    int Present, int Absent, int Leave, int HalfDays);
public record PayrollEntryRow(int Id, string Date, string Month, string Type, decimal Amount, string Method, string Note, string CreatedBy);
public record PayrollMonth(string Month, IReadOnlyList<PayrollRow> Rows, decimal TotalSalary, decimal TotalPaid, decimal TotalDue);
public record AttendanceRow(int EmployeeId, string Date, string Status);

/// <summary>Salaries, advances and attendance, month by month.</summary>
public class PayrollService
{
    private static readonly string[] Statuses = { "Present", "Absent", "Leave", "HalfDay" };

    private readonly IAppDbContext _db;
    public PayrollService(IAppDbContext db) => _db = db;

    public async Task<PayrollMonth> Month(string? month, CancellationToken ct = default)
    {
        var key = MonthKey(month);
        var (first, last) = Bounds(key);
        var people = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync(ct);
        var entries = await _db.EmployeeEntries.AsNoTracking().Where(e => e.Month == key).ToListAsync(ct);
        var marks = await _db.AttendanceMarks.AsNoTracking().Where(a => a.Date >= first && a.Date <= last).ToListAsync(ct);

        var rows = people.Select(p =>
        {
            var mine = entries.Where(e => e.EmployeeId == p.Id).ToList();
            var att = marks.Where(a => a.EmployeeId == p.Id).ToList();
            decimal Sum(EmployeeEntryType t) => mine.Where(e => e.Type == t).Sum(e => e.Amount);
            var advances = Sum(EmployeeEntryType.Advance);
            var bonuses = Sum(EmployeeEntryType.Bonus);
            var deductions = Sum(EmployeeEntryType.Deduction);
            var paid = Sum(EmployeeEntryType.Salary);
            return new PayrollRow(p.Id, p.Name, p.Phone, p.Designation, p.MonthlySalary,
                p.JoinedOn?.ToString("yyyy-MM-dd"), p.Notes, advances, bonuses, deductions, paid,
                p.MonthlySalary + bonuses - deductions - advances - paid,
                att.Count(a => a.Status == "Present"), att.Count(a => a.Status == "Absent"),
                att.Count(a => a.Status == "Leave"), att.Count(a => a.Status == "HalfDay"));
        }).ToList();

        return new PayrollMonth(key, rows, rows.Sum(r => r.MonthlySalary), rows.Sum(r => r.Paid + r.Advances),
            rows.Sum(r => Math.Max(0, r.Due)));
    }

    public async Task<int> Create(EmployeeInput input, CancellationToken ct = default)
    {
        var e = new Employee();
        Apply(e, input);
        _db.Employees.Add(e);
        await _db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task Update(int id, EmployeeInput input, CancellationToken ct = default)
    {
        Apply(await Get(id, ct), input);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Archive(int id, CancellationToken ct = default)
    {
        (await Get(id, ct)).IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PayrollEntryRow>> Entries(int employeeId, string? month, CancellationToken ct = default)
    {
        var key = MonthKey(month);
        var rows = await _db.EmployeeEntries.AsNoTracking().Where(e => e.EmployeeId == employeeId && e.Month == key).ToListAsync(ct);
        return rows.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new PayrollEntryRow(e.Id, e.Date.ToString("yyyy-MM-dd"), e.Month, e.Type.ToString(), e.Amount, e.PaymentMethod, e.Note, e.CreatedBy))
            .ToList();
    }

    /// <summary>Records a payment or deduction. Cash that leaves the till also becomes an expense.</summary>
    public async Task<int> AddEntry(int employeeId, PayrollEntryInput input, string createdBy, CancellationToken ct = default)
    {
        var person = await Get(employeeId, ct);
        if (!Enum.TryParse<EmployeeEntryType>(input.Type, true, out var type))
            throw new DomainException("Choose advance, salary, bonus or deduction.");
        if (input.Amount <= 0) throw new DomainException("Enter an amount greater than zero.");

        var date = BusinessDay.Parse(input.Date);
        var key = string.IsNullOrWhiteSpace(input.Month) ? date.ToString("yyyy-MM") : MonthKey(input.Month);
        var method = string.IsNullOrWhiteSpace(input.Method) ? "Cash" : input.Method.Trim();
        var note = (input.Note ?? "").Trim();

        var entry = new EmployeeEntry
        {
            EmployeeId = employeeId, Type = type, Date = date, Month = key, Amount = input.Amount,
            PaymentMethod = method, Note = note, CreatedBy = createdBy,
        };
        if (entry.PaysOutCash)
        {
            var expense = new Expense
            {
                Date = date, Category = "Salaries", Amount = input.Amount, PaymentMethod = method,
                PaidTo = person.Name, Note = $"{type} for {key}" + (note.Length > 0 ? $": {note}" : ""), CreatedBy = createdBy,
            };
            _db.Expenses.Add(expense);
            await _db.SaveChangesAsync(ct);
            entry.ExpenseId = expense.Id;
        }
        _db.EmployeeEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry.Id;
    }

    public async Task DeleteEntry(int entryId, CancellationToken ct = default)
    {
        var entry = await _db.EmployeeEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new DomainException("That entry no longer exists.");
        if (entry.ExpenseId is int expenseId)
        {
            var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId, ct);
            if (expense is not null) _db.Expenses.Remove(expense);
        }
        _db.EmployeeEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AttendanceRow>> Attendance(string? month, CancellationToken ct = default)
    {
        var (first, last) = Bounds(MonthKey(month));
        var rows = await _db.AttendanceMarks.AsNoTracking().Where(a => a.Date >= first && a.Date <= last).ToListAsync(ct);
        return rows.Select(a => new AttendanceRow(a.EmployeeId, a.Date.ToString("yyyy-MM-dd"), a.Status)).ToList();
    }

    /// <summary>Marks a day. A blank status clears it.</summary>
    public async Task SetAttendance(AttendanceInput input, CancellationToken ct = default)
    {
        await Get(input.EmployeeId, ct);
        var date = BusinessDay.Parse(input.Date);
        var mark = await _db.AttendanceMarks.FirstOrDefaultAsync(a => a.EmployeeId == input.EmployeeId && a.Date == date, ct);
        if (string.IsNullOrWhiteSpace(input.Status))
        {
            if (mark is not null) _db.AttendanceMarks.Remove(mark);
        }
        else
        {
            if (!Statuses.Contains(input.Status)) throw new DomainException("Choose present, absent, leave or half day.");
            if (mark is null) _db.AttendanceMarks.Add(new AttendanceMark { EmployeeId = input.EmployeeId, Date = date, Status = input.Status });
            else mark.Status = input.Status;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Employee> Get(int id, CancellationToken ct) =>
        await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new DomainException("Staff member not found.");

    private static void Apply(Employee e, EmployeeInput input)
    {
        e.Name = input.Name.Trim();
        e.Phone = (input.Phone ?? "").Trim();
        e.Designation = (input.Designation ?? "").Trim();
        e.MonthlySalary = Math.Max(0, input.MonthlySalary);
        e.JoinedOn = string.IsNullOrWhiteSpace(input.JoinedOn) ? null : BusinessDay.Parse(input.JoinedOn);
        e.Notes = (input.Notes ?? "").Trim();
    }

    private static string MonthKey(string? month)
    {
        if (string.IsNullOrWhiteSpace(month)) return BusinessDay.Today.ToString("yyyy-MM");
        if (DateTime.TryParseExact(month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM");
        throw new DomainException("That month doesn't look right.");
    }

    private static (DateTime First, DateTime Last) Bounds(string key)
    {
        var first = DateTime.ParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture);
        return (first, first.AddMonths(1).AddDays(-1));
    }
}
