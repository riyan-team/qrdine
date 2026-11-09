using Microsoft.EntityFrameworkCore;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Customers;

namespace QrDine.Application.Customers;

public record CustomerInput(string Name, string? Phone, string? Address, string? Notes, decimal OpeningBalance, decimal CreditLimit);
public record CustomerPaymentInput(string? Date, decimal Amount, string? Method, string? Reference, string? Note);
public record CustomerRow(int Id, string Name, string Phone, string Address, string Notes, decimal OpeningBalance,
    decimal CreditLimit, decimal Balance, string? LastEntry, int Entries);
public record CustomerMatch(int Id, string Name, string Phone, string Address, decimal Balance, decimal CreditLimit);
public record CustomerLedgerRow(int Id, string Date, string Type, string Reference, string Note, string Method,
    decimal Charged, decimal Paid, decimal Balance, string CreatedBy, int? InvoiceId);
public record CustomerLedger(CustomerRow Customer, IReadOnlyList<CustomerLedgerRow> Rows);

/// <summary>The customer khata: bills put on credit and the money that comes back against them.</summary>
public class CustomerService
{
    private readonly IAppDbContext _db;
    public CustomerService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CustomerRow>> List(CancellationToken ct = default)
    {
        var customers = await _db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(ct);
        var entries = await _db.CustomerEntries.AsNoTracking().ToListAsync(ct);
        return customers.Select(c => ToRow(c, entries.Where(e => e.CustomerId == c.Id).ToList()))
            .OrderByDescending(c => c.Balance).ThenBy(c => c.Name).ToList();
    }

    /// <summary>Reception types part of a name or number and picks the right person.</summary>
    public async Task<IReadOnlyList<CustomerMatch>> Find(string? q, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        if (term.Length < 2) return Array.Empty<CustomerMatch>();
        var phone = LoginName.Normalize(term);
        var lower = term.ToLower();
        var matches = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive && (c.Phone.Contains(phone) || c.Name.ToLower().Contains(lower)))
            .OrderBy(c => c.Name).Take(8).ToListAsync(ct);
        var ids = matches.Select(c => c.Id).ToList();
        var entries = await _db.CustomerEntries.AsNoTracking().Where(e => ids.Contains(e.CustomerId)).ToListAsync(ct);
        return matches.Select(c => new CustomerMatch(c.Id, c.Name, c.Phone, c.Address,
            c.OpeningBalance + entries.Where(e => e.CustomerId == c.Id).Sum(e => e.BalanceEffect), c.CreditLimit)).ToList();
    }

    public async Task<int> Create(CustomerInput input, CancellationToken ct = default)
    {
        var phone = LoginName.Normalize(input.Phone);
        if (phone.Length > 0 && await _db.Customers.AnyAsync(c => c.Phone == phone && c.IsActive, ct))
            throw new DomainException("A customer with this number already exists.");
        var c = new Customer();
        Apply(c, input, phone);
        _db.Customers.Add(c);
        await _db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task Update(int id, CustomerInput input, CancellationToken ct = default)
    {
        var c = await Get(id, ct);
        var phone = LoginName.Normalize(input.Phone);
        if (phone.Length > 0 && await _db.Customers.AnyAsync(x => x.Id != id && x.Phone == phone && x.IsActive, ct))
            throw new DomainException("A customer with this number already exists.");
        Apply(c, input, phone);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Hides the customer. Refused while they still owe, so a balance can't quietly vanish.</summary>
    public async Task Archive(int id, CancellationToken ct = default)
    {
        var c = await Get(id, ct);
        var entries = await _db.CustomerEntries.AsNoTracking().Where(e => e.CustomerId == id).ToListAsync(ct);
        if (c.OpeningBalance + entries.Sum(e => e.BalanceEffect) > 0)
            throw new DomainException("This customer still owes money. Clear the balance first.");
        c.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<CustomerLedger> Ledger(int id, CancellationToken ct = default)
    {
        var c = await Get(id, ct);
        var entries = await _db.CustomerEntries.AsNoTracking().Where(e => e.CustomerId == id).ToListAsync(ct);
        var running = c.OpeningBalance;
        var rows = new List<CustomerLedgerRow>();
        foreach (var e in entries.OrderBy(x => x.Date).ThenBy(x => x.Id))
        {
            running += e.BalanceEffect;
            rows.Add(new CustomerLedgerRow(e.Id, e.Date.ToString("yyyy-MM-dd"), e.Type.ToString(), e.Reference, e.Note, e.PaymentMethod,
                e.BalanceEffect > 0 ? e.BalanceEffect : 0, e.BalanceEffect < 0 ? -e.BalanceEffect : 0, running, e.CreatedBy, e.InvoiceId));
        }
        return new CustomerLedger(ToRow(c, entries), rows);
    }

    public async Task<int> AddPayment(int customerId, CustomerPaymentInput input, string createdBy, CancellationToken ct = default)
    {
        await Get(customerId, ct);
        if (input.Amount <= 0) throw new DomainException("Enter how much they paid.");
        var entry = new CustomerEntry
        {
            CustomerId = customerId, Type = CustomerEntryType.Payment, Date = BusinessDay.Parse(input.Date), Amount = input.Amount,
            PaymentMethod = string.IsNullOrWhiteSpace(input.Method) ? "Cash" : input.Method.Trim(),
            Reference = (input.Reference ?? "").Trim(), Note = (input.Note ?? "").Trim(), CreatedBy = createdBy,
        };
        _db.CustomerEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry.Id;
    }

    /// <summary>Only a payment can be taken back. A bill on khata is part of a settled invoice, and that stays.</summary>
    public async Task DeleteEntry(int entryId, CancellationToken ct = default)
    {
        var entry = await _db.CustomerEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new DomainException("That entry no longer exists.");
        if (entry.Type == CustomerEntryType.Sale)
            throw new DomainException("A bill on khata can't be removed here because it belongs to a settled invoice.");
        _db.CustomerEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Customer> Get(int id, CancellationToken ct) =>
        await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new DomainException("Customer not found.");

    private static void Apply(Customer c, CustomerInput input, string phone)
    {
        c.Name = input.Name.Trim();
        c.Phone = phone;
        c.Address = (input.Address ?? "").Trim();
        c.Notes = (input.Notes ?? "").Trim();
        c.OpeningBalance = input.OpeningBalance;
        c.CreditLimit = Math.Max(0, input.CreditLimit);
    }

    private static CustomerRow ToRow(Customer c, IReadOnlyCollection<CustomerEntry> entries) => new(
        c.Id, c.Name, c.Phone, c.Address, c.Notes, c.OpeningBalance, c.CreditLimit,
        c.OpeningBalance + entries.Sum(e => e.BalanceEffect),
        entries.Count == 0 ? null : entries.Max(e => e.Date).ToString("yyyy-MM-dd"), entries.Count);
}
