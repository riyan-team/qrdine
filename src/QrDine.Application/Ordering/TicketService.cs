using Microsoft.EntityFrameworkCore;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Application.Ordering.Dtos;
using QrDine.Domain.Common;
using QrDine.Domain.Customers;
using QrDine.Domain.Ordering;
using QrDine.Domain.Tables;

namespace QrDine.Application.Ordering;

public record TicketRequest(string Channel, string? CustomerName, string? CustomerPhone, string? Address,
    decimal? DeliveryCharge, int? BranchId, List<OrderLineRequest> Items, string? Note);
public record TicketUpdate(string? CustomerName, string? CustomerPhone, string? Address, decimal? DeliveryCharge, string? RiderName);
public record TicketRow(string TableCode, string TableName, string Channel, string CustomerName, string CustomerPhone,
    string Address, string RiderName, decimal DeliveryCharge, DateTime OpenedAt);
public record TicketPlaced(OrderDto Order, TicketRow Ticket);

/// <summary>
/// Parcel and home delivery. Each ticket gets a private table of its own (Parcel 12, Delivery 3)
/// and is then an ordinary order: same kitchen, same bill, same reports. Numbers restart each day
/// like a token book, and because a ticket's table is never reused, two customers' orders can't
/// land on one bill.
/// </summary>
public class TicketService
{
    private readonly IAppDbContext _db;
    private readonly OrderService _orders;

    public TicketService(IAppDbContext db, OrderService orders) { _db = db; _orders = orders; }

    public async Task<TicketPlaced> Place(TicketRequest req, CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrderChannel>(req.Channel, true, out var channel) || channel == OrderChannel.Dine)
            throw new DomainException("Choose parcel or delivery.");
        if (req.Items is null || req.Items.Count == 0)
            throw new DomainException("Add something to the order first.");

        var phone = LoginName.Normalize(req.CustomerPhone);
        var name = (req.CustomerName ?? "").Trim();
        var address = (req.Address ?? "").Trim();
        if (channel == OrderChannel.Delivery && (phone.Length == 0 || address.Length == 0))
            throw new DomainException("A delivery needs the customer's phone number and address.");
        var charge = Math.Max(0, req.DeliveryCharge ?? 0);

        var branchId = await ResolveBranch(req.BranchId, ct);
        var table = await NewTicketTable(channel, branchId, ct);

        var session = TableSession.Open(table);
        session.CustomerName = name;
        session.CustomerPhone = phone;
        session.DeliveryAddress = address;
        session.DeliveryCharge = channel == OrderChannel.Delivery ? charge : 0;
        if (phone.Length > 0) session.CustomerId = await RememberCustomer(name, phone, address, ct);
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);

        try
        {
            var order = await _orders.Place(new PlaceOrderRequest(table.Code, req.Items, req.Note), OrderSource.Counter, null, ct);
            return new TicketPlaced(order, ToRow(session));
        }
        catch (DomainException)
        {
            // Nothing on the order could be made: don't leave an empty ticket behind.
            _db.Sessions.Remove(session);
            _db.Tables.Remove(table);
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<TicketRow>> Open(CancellationToken ct = default)
    {
        var sessions = await _db.Sessions.AsNoTracking()
            .Where(s => s.IsOpen && s.Channel != OrderChannel.Dine).OrderBy(s => s.OpenedAt).ToListAsync(ct);
        return sessions.Select(ToRow).ToList();
    }

    public async Task<TicketRow> Update(string code, TicketUpdate input, CancellationToken ct = default)
    {
        var session = await _db.Sessions.Where(s => s.TableCode == code && s.IsOpen && s.Channel != OrderChannel.Dine)
            .FirstOrDefaultAsync(ct) ?? throw new DomainException("That ticket is already closed.");
        if (input.CustomerName is not null) session.CustomerName = input.CustomerName.Trim();
        if (input.CustomerPhone is not null) session.CustomerPhone = LoginName.Normalize(input.CustomerPhone);
        if (input.Address is not null) session.DeliveryAddress = input.Address.Trim();
        if (input.RiderName is not null) session.RiderName = input.RiderName.Trim();
        if (input.DeliveryCharge is decimal c && session.Channel == OrderChannel.Delivery) session.DeliveryCharge = Math.Max(0, c);
        await _db.SaveChangesAsync(ct);
        return ToRow(session);
    }

    private async Task<int> ResolveBranch(int? wanted, CancellationToken ct)
    {
        if (wanted is int id && await _db.Branches.AnyAsync(b => b.Id == id, ct)) return id;
        var first = await _db.Branches.OrderBy(b => b.Id).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Create a branch first.");
        return first.Id;
    }

    /// <summary>Two counters may ring up at the same moment: the unique table code decides, and the loser takes the next number.</summary>
    private async Task<DiningTable> NewTicketTable(OrderChannel channel, int branchId, CancellationToken ct)
    {
        var label = channel == OrderChannel.Takeaway ? "Parcel" : "Delivery";
        var prefix = $"{(channel == OrderChannel.Takeaway ? "P" : "D")}{BusinessDay.Today:yyMMdd}-";
        var number = await _db.Tables.CountAsync(t => t.Code.StartsWith(prefix), ct) + 1;

        var table = new DiningTable { BranchId = branchId, Kind = channel };
        _db.Tables.Add(table);
        for (var attempt = 1; ; attempt++)
        {
            table.Code = prefix + number;
            table.Name = $"{label} {number}";
            try { await _db.SaveChangesAsync(ct); return table; }
            catch (DbUpdateException) when (attempt < 8) { number++; }
        }
    }

    private async Task<int> RememberCustomer(string name, string phone, string address, CancellationToken ct)
    {
        var customer = await _db.Customers.Where(c => c.Phone == phone && c.IsActive).FirstOrDefaultAsync(ct);
        if (customer is null)
        {
            customer = new Customer { Name = name.Length > 0 ? name : phone, Phone = phone, Address = address };
            _db.Customers.Add(customer);
        }
        else
        {
            if (name.Length > 0) customer.Name = name;
            if (address.Length > 0) customer.Address = address;
        }
        await _db.SaveChangesAsync(ct);
        return customer.Id;
    }

    private static TicketRow ToRow(TableSession s) => new(s.TableCode, s.TableName, s.Channel.ToString(), s.CustomerName,
        s.CustomerPhone, s.DeliveryAddress, s.RiderName, s.DeliveryCharge,
        DateTime.SpecifyKind(s.OpenedAt, DateTimeKind.Utc));
}
