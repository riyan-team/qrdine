using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Inventory;
using QrDine.Domain.Purchasing;

namespace QrDine.Application.Purchasing;

public record SupplierInput(string Name, string? Phone, string? Notes, decimal OpeningBalance);
public record SupplierRow(int Id, string Name, string Phone, string Notes, decimal OpeningBalance,
    decimal Balance, string? LastEntry, int Entries);

public record PurchaseLineInput(int? StockItemId, string? Name, decimal Qty, decimal UnitCost);
public record PurchaseInput(string? Date, string? Reference, string? Note, decimal? Amount,
    List<PurchaseLineInput>? Lines, decimal? PaidNow, string? PaidMethod);
public record SupplierPaymentInput(string? Date, decimal Amount, string? Method, string? Reference, string? Note);
public record SupplierReturnInput(string? Date, string? Reference, string? Note, decimal? Amount, List<PurchaseLineInput>? Lines);

public record LedgerLine(string Name, decimal Qty, decimal UnitCost, decimal Total);
public record LedgerRow(int Id, string Date, string Type, string Reference, string Note, string Method,
    decimal Bought, decimal Paid, decimal Balance, string CreatedBy, IReadOnlyList<LedgerLine> Lines);
public record SupplierLedger(SupplierRow Supplier, IReadOnlyList<LedgerRow> Rows);

/// <summary>
/// The supplier khata. What's owed is never stored: it's always the opening balance plus every
/// entry, so it can't drift. A bill line tied to a stock item moves that stock through a normal
/// stock movement, and deleting the bill moves it back.
/// </summary>
public class SupplierService
{
    private readonly IAppDbContext _db;
    public SupplierService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SupplierRow>> List(CancellationToken ct = default)
    {
        var suppliers = await _db.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);
        var entries = await _db.SupplierEntries.AsNoTracking().ToListAsync(ct);
        return suppliers.Select(s => ToRow(s, entries.Where(e => e.SupplierId == s.Id).ToList())).ToList();
    }

    public async Task<int> Create(SupplierInput input, CancellationToken ct = default)
    {
        var s = new Supplier
        {
            Name = input.Name.Trim(), Phone = (input.Phone ?? "").Trim(), Notes = (input.Notes ?? "").Trim(),
            OpeningBalance = input.OpeningBalance,
        };
        _db.Suppliers.Add(s);
        await _db.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task Update(int id, SupplierInput input, CancellationToken ct = default)
    {
        var s = await Get(id, ct);
        s.Name = input.Name.Trim();
        s.Phone = (input.Phone ?? "").Trim();
        s.Notes = (input.Notes ?? "").Trim();
        s.OpeningBalance = input.OpeningBalance;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Hides the supplier. Their history stays, because reports still need it.</summary>
    public async Task Archive(int id, CancellationToken ct = default)
    {
        var s = await Get(id, ct);
        s.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SupplierLedger> Ledger(int id, CancellationToken ct = default)
    {
        var s = await Get(id, ct);
        var entries = await _db.SupplierEntries.AsNoTracking().Include(e => e.Lines)
            .Where(e => e.SupplierId == id).ToListAsync(ct);

        var running = s.OpeningBalance;
        var rows = new List<LedgerRow>();
        foreach (var e in entries.OrderBy(x => x.Date).ThenBy(x => x.Id))
        {
            running += e.BalanceEffect;
            rows.Add(new LedgerRow(e.Id, e.Date.ToString("yyyy-MM-dd"), e.Type.ToString(), e.Reference, e.Note, e.PaymentMethod,
                e.BalanceEffect > 0 ? e.BalanceEffect : 0,
                e.BalanceEffect < 0 ? -e.BalanceEffect : 0,
                running, e.CreatedBy,
                e.Lines.Select(l => new LedgerLine(l.Name, l.Qty, l.UnitCost, l.LineTotal)).ToList()));
        }
        return new SupplierLedger(ToRow(s, entries), rows);
    }

    public async Task<int> AddPurchase(int supplierId, PurchaseInput input, string createdBy, CancellationToken ct = default)
    {
        var supplier = await Get(supplierId, ct);
        var date = BusinessDay.Parse(input.Date);
        var lines = await BuildLines(input.Lines, ct);
        var amount = lines.Count > 0 ? lines.Sum(l => l.LineTotal) : input.Amount ?? 0;
        if (amount <= 0) throw new DomainException("Enter what the bill came to, or add what was bought.");

        var paidNow = input.PaidNow ?? 0;
        if (paidNow < 0) throw new DomainException("The amount paid can't be negative.");
        if (paidNow > amount) throw new DomainException("You can't pay more than the bill at the same time. Record the extra as a separate payment.");

        var entry = new SupplierEntry
        {
            SupplierId = supplierId, Type = SupplierEntryType.Purchase, Date = date, Amount = amount,
            Reference = (input.Reference ?? "").Trim(), Note = (input.Note ?? "").Trim(), CreatedBy = createdBy, Lines = lines,
        };
        _db.SupplierEntries.Add(entry);
        await MoveStock(lines, +1, StockMovementType.Restock, $"Bought from {supplier.Name}", updateCost: true, ct);
        await _db.SaveChangesAsync(ct);

        if (paidNow > 0)
        {
            var payment = new SupplierEntry
            {
                SupplierId = supplierId, Type = SupplierEntryType.Payment, Date = date, Amount = paidNow,
                PaymentMethod = string.IsNullOrWhiteSpace(input.PaidMethod) ? "Cash" : input.PaidMethod.Trim(),
                Reference = entry.Reference, Note = "Paid with the bill", CreatedBy = createdBy,
                LinkedEntryId = entry.Id,
            };
            _db.SupplierEntries.Add(payment);
            await _db.SaveChangesAsync(ct);
        }
        return entry.Id;
    }

    public async Task<int> AddPayment(int supplierId, SupplierPaymentInput input, string createdBy, CancellationToken ct = default)
    {
        await Get(supplierId, ct);
        var entry = new SupplierEntry
        {
            SupplierId = supplierId, Type = SupplierEntryType.Payment, Date = BusinessDay.Parse(input.Date),
            Amount = input.Amount, PaymentMethod = string.IsNullOrWhiteSpace(input.Method) ? "Cash" : input.Method.Trim(),
            Reference = (input.Reference ?? "").Trim(), Note = (input.Note ?? "").Trim(), CreatedBy = createdBy,
        };
        _db.SupplierEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry.Id;
    }

    public async Task<int> AddReturn(int supplierId, SupplierReturnInput input, string createdBy, CancellationToken ct = default)
    {
        var supplier = await Get(supplierId, ct);
        var lines = await BuildLines(input.Lines, ct);
        var amount = lines.Count > 0 ? lines.Sum(l => l.LineTotal) : input.Amount ?? 0;
        if (amount <= 0) throw new DomainException("Enter the value of what went back.");

        var entry = new SupplierEntry
        {
            SupplierId = supplierId, Type = SupplierEntryType.Return, Date = BusinessDay.Parse(input.Date), Amount = amount,
            Reference = (input.Reference ?? "").Trim(), Note = (input.Note ?? "").Trim(), CreatedBy = createdBy, Lines = lines,
        };
        _db.SupplierEntries.Add(entry);
        await MoveStock(lines, -1, StockMovementType.Adjust, $"Returned to {supplier.Name}", updateCost: false, ct);
        await _db.SaveChangesAsync(ct);
        return entry.Id;
    }

    /// <summary>Removes an entry, puts any stock back where it was, and takes the linked payment with it.</summary>
    public async Task DeleteEntry(int entryId, CancellationToken ct = default)
    {
        var entry = await _db.SupplierEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new DomainException("That entry no longer exists.");

        var direction = entry.Type switch
        {
            SupplierEntryType.Purchase => -1,
            SupplierEntryType.Return => +1,
            _ => 0,
        };
        if (direction != 0)
            await MoveStock(entry.Lines, direction, StockMovementType.Adjust, $"{entry.Type} entry deleted", updateCost: false, ct);

        var linked = await _db.SupplierEntries.Where(e => e.LinkedEntryId == entry.Id).ToListAsync(ct);
        _db.SupplierEntries.RemoveRange(linked);
        _db.SupplierEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Supplier> Get(int id, CancellationToken ct) =>
        await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new DomainException("Supplier not found.");

    private static SupplierRow ToRow(Supplier s, IReadOnlyCollection<SupplierEntry> entries) => new(
        s.Id, s.Name, s.Phone, s.Notes, s.OpeningBalance,
        s.OpeningBalance + entries.Sum(e => e.BalanceEffect),
        entries.Count == 0 ? null : entries.Max(e => e.Date).ToString("yyyy-MM-dd"),
        entries.Count);

    private async Task<List<SupplierEntryLine>> BuildLines(List<PurchaseLineInput>? input, CancellationToken ct)
    {
        var lines = new List<SupplierEntryLine>();
        if (input is null || input.Count == 0) return lines;

        var ids = input.Where(l => l.StockItemId != null).Select(l => l.StockItemId!.Value).Distinct().ToList();
        var stock = await _db.Stock.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var l in input)
        {
            if (l.Qty <= 0) throw new DomainException("Quantity must be more than zero.");
            if (l.UnitCost < 0) throw new DomainException("Price can't be negative.");
            string name;
            if (l.StockItemId is int sid)
            {
                if (!stock.TryGetValue(sid, out var item)) throw new DomainException("That stock item no longer exists.");
                name = item.Name;
            }
            else
            {
                name = (l.Name ?? "").Trim();
                if (name.Length == 0) throw new DomainException("Say what was bought on each line.");
            }
            lines.Add(new SupplierEntryLine { StockItemId = l.StockItemId, Name = name, Qty = l.Qty, UnitCost = l.UnitCost });
        }
        return lines;
    }

    private async Task MoveStock(IEnumerable<SupplierEntryLine> lines, int direction, StockMovementType type,
        string reason, bool updateCost, CancellationToken ct)
    {
        var tied = lines.Where(l => l.StockItemId != null).ToList();
        if (tied.Count == 0) return;

        var ids = tied.Select(l => l.StockItemId!.Value).Distinct().ToList();
        var stock = await _db.Stock.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var l in tied)
        {
            if (!stock.TryGetValue(l.StockItemId!.Value, out var item)) continue;   // since deleted: nothing to move
            var delta = direction * l.Qty;
            item.Quantity += delta;
            if (updateCost && l.UnitCost > 0) item.LastCost = l.UnitCost;
            _db.StockMovements.Add(new StockMovement { StockItemId = item.Id, Delta = delta, Type = type, Reason = reason });
        }
    }
}
