using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Expenses;

namespace QrDine.Application.Expenses;

public record ExpenseInput(string? Date, string Category, decimal Amount, string? PaymentMethod,
    string? PaidTo, string? Note, int? BranchId);
public record ExpenseRow(int Id, string Date, string Category, decimal Amount, string PaymentMethod,
    string PaidTo, string Note, int? BranchId, string CreatedBy);
public record ExpenseCategoryTotal(string Category, decimal Amount, int Count);
public record ExpensePage(IReadOnlyList<ExpenseRow> Rows, decimal Total,
    IReadOnlyList<ExpenseCategoryTotal> ByCategory, IReadOnlyList<string> Categories);

public class ExpenseService
{
    private readonly IAppDbContext _db;
    public ExpenseService(IAppDbContext db) => _db = db;

    public async Task<ExpensePage> List(string? from, string? to, string? category, int? branchId, CancellationToken ct = default)
    {
        var start = string.IsNullOrWhiteSpace(from) ? BusinessDay.Today.AddDays(-30) : BusinessDay.Parse(from);
        var end = BusinessDay.Parse(to);
        var query = _db.Expenses.AsNoTracking().Where(e => e.Date >= start && e.Date <= end);
        if (branchId != null) query = query.Where(e => e.BranchId == null || e.BranchId == branchId);
        var all = await query.ToListAsync(ct);

        var shown = string.IsNullOrWhiteSpace(category) ? all : all.Where(e => e.Category == category).ToList();
        var used = await _db.Expenses.AsNoTracking().Select(e => e.Category).Distinct().ToListAsync(ct);
        var categories = Expense.DefaultCategories
            .Concat(used.Where(c => !Expense.DefaultCategories.Contains(c)).OrderBy(c => c)).ToList();

        return new ExpensePage(
            shown.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Select(ToRow).ToList(),
            shown.Sum(e => e.Amount),
            all.GroupBy(e => e.Category).Select(g => new ExpenseCategoryTotal(g.Key, g.Sum(e => e.Amount), g.Count()))
               .OrderByDescending(g => g.Amount).ToList(),
            categories);
    }

    public async Task<int> Create(ExpenseInput input, string createdBy, CancellationToken ct = default)
    {
        var e = new Expense { CreatedBy = createdBy };
        Apply(e, input);
        _db.Expenses.Add(e);
        await _db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task Update(int id, ExpenseInput input, CancellationToken ct = default)
    {
        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("That expense no longer exists.");
        await RefuseIfSalary(id, ct);
        Apply(e, input);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Delete(int id, CancellationToken ct = default)
    {
        var e = await _db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("That expense no longer exists.");
        await RefuseIfSalary(id, ct);
        _db.Expenses.Remove(e);
        await _db.SaveChangesAsync(ct);
    }

    private async Task RefuseIfSalary(int expenseId, CancellationToken ct)
    {
        if (await _db.EmployeeEntries.AnyAsync(x => x.ExpenseId == expenseId, ct))
            throw new DomainException("This is a salary payment. Change it under Staff & salaries.");
    }

    private static void Apply(Expense e, ExpenseInput input)
    {
        e.Date = BusinessDay.Parse(input.Date);
        e.Category = input.Category.Trim();
        e.Amount = input.Amount;
        e.PaymentMethod = string.IsNullOrWhiteSpace(input.PaymentMethod) ? "Cash" : input.PaymentMethod.Trim();
        e.PaidTo = (input.PaidTo ?? "").Trim();
        e.Note = (input.Note ?? "").Trim();
        e.BranchId = input.BranchId;
    }

    private static ExpenseRow ToRow(Expense e) => new(e.Id, e.Date.ToString("yyyy-MM-dd"), e.Category, e.Amount,
        e.PaymentMethod, e.PaidTo, e.Note, e.BranchId, e.CreatedBy);
}
