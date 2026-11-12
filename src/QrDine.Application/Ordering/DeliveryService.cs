using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Ordering;
using QrDine.Domain.Tables;

namespace QrDine.Application.Ordering;

public record GeoPoint(double Lat, double Lng);
public record PromiseInput(int Minutes);
public record RiderLinkInput(string? RiderName, string? RiderPhone);
public record RiderLinkDto(string Path, string RiderName, string RiderPhone);
public record RiderLocationInput(double Lat, double Lng);

/// <summary>What the guest's phone shows while they wait for a takeaway or a delivery.</summary>
/// <param name="Stage">Received, Confirmed, Cooking, Ready, OnTheWay, Delivered, Collected or Closed.</param>
/// <param name="EtaAt">Best guess of when it's ready (takeaway) or at the door (delivery).</param>
public record TrackDto(string TableCode, string TableName, string Channel, string Stage,
    DateTime PlacedAt, DateTime? ReadyAt, DateTime? PromisedAt, DateTime? EtaAt, DateTime? DispatchedAt, DateTime? DeliveredAt,
    string RiderName, GeoPoint? Shop, GeoPoint? Home, GeoPoint? Rider, DateTime? RiderSeenAt, double? DistanceKm);

/// <summary>The rider's screen: where to go, who to call, what to collect.</summary>
public record RideDto(string TableCode, string TableName, string RestaurantName, string CustomerName, string CustomerPhone,
    string Address, GeoPoint? Shop, GeoPoint? Home, double? DistanceKm, List<string> Items, string Note, decimal Collect,
    DateTime? PromisedAt, DateTime? DispatchedAt, DateTime? DeliveredAt, bool FoodReady, string Stage);

public static class Geo
{
    /// <summary>Straight-line distance on the earth's surface, in km.</summary>
    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 6371.0 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
    public static bool Valid(double? lat, double? lng) =>
        lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);
    public static GeoPoint? Point(double? lat, double? lng) => Valid(lat, lng) ? new GeoPoint(lat!.Value, lng!.Value) : null;
    public static double? Between(GeoPoint? a, GeoPoint? b) =>
        a is null || b is null ? null : Math.Round(DistanceKm(a.Lat, a.Lng, b.Lat, b.Lng), 1);
    /// <summary>City riding time: about 3 minutes a km on a bike, plus parking and the door.</summary>
    public static int RideMinutes(double km) => (int)Math.Ceiling(km * 3 + 4);
}

/// <summary>
/// Takeaway and delivery after the order is placed: the time promised to the customer, the rider's
/// trip (a private link, their live position, picked up, delivered) and what the customer sees.
/// </summary>
public class DeliveryService
{
    private readonly IAppDbContext _db;
    private readonly OrderService _orders;
    private readonly ICurrentTenant _tenant;

    public DeliveryService(IAppDbContext db, OrderService orders, ICurrentTenant tenant) { _db = db; _orders = orders; _tenant = tenant; }

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");
    private static DateTime? U(DateTime? d) => d is null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);

    /// <summary>A ticket has exactly one session (its table is never reused).</summary>
    private Task<TableSession?> Session(string code, CancellationToken ct) =>
        _db.Sessions.Where(s => s.TableCode == code && s.Channel != OrderChannel.Dine)
            .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct);

    private async Task<TableSession> OpenTicket(string code, CancellationToken ct) =>
        await Session(code, ct) is { IsOpen: true } s ? s : throw new DomainException("That order is already closed.");

    public async Task<GeoPoint?> ShopPoint(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        return Geo.Point(r.Latitude, r.Longitude);
    }

    private Task<List<Order>> OrdersOf(TableSession s, CancellationToken ct) =>
        _db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.TableSessionId == s.Id && o.Status != OrderStatus.Cancelled).OrderBy(o => o.Id).ToListAsync(ct);

    public static string StageOf(TableSession s, IReadOnlyCollection<Order> orders)
    {
        var done = s.Channel == OrderChannel.Takeaway ? "Collected" : "Delivered";
        if (s.DeliveredAt is not null) return done;
        if (orders.Count == 0) return s.IsOpen ? "Received" : "Closed";
        if (!s.IsOpen) return done;                                       // settled
        if (s.DispatchedAt is not null) return "OnTheWay";
        if (orders.Any(o => o.Status == OrderStatus.Placed)) return "Received";
        if (orders.Any(o => o.Status == OrderStatus.Approved)) return "Confirmed";
        if (orders.Any(o => o.Status == OrderStatus.Cooking)) return "Cooking";
        if (orders.All(o => o.Status is OrderStatus.Served or OrderStatus.Paid)) return s.Channel == OrderChannel.Takeaway ? "Collected" : "OnTheWay";
        return "Ready";
    }

    private static DateTime? ReadyAt(IEnumerable<Order> orders) =>
        orders.Where(o => o.Status == OrderStatus.Cooking && o.AcceptedAt != null)
            .Select(o => (DateTime?)DateTime.SpecifyKind(o.AcceptedAt!.Value, DateTimeKind.Utc).AddMinutes(o.EtaMinutes)).Max();

    // ---------- the guest ----------

    public async Task<TrackDto> Track(string code, CancellationToken ct = default)
    {
        var s = await Session(code, ct) ?? throw new DomainException("Order not found.");
        var orders = await OrdersOf(s, ct);
        var shop = await ShopPoint(ct);
        var delivery = s.Channel == OrderChannel.Delivery;
        var home = delivery ? Geo.Point(s.DeliveryLat, s.DeliveryLng) : null;
        var stage = StageOf(s, orders);
        var readyAt = ReadyAt(orders);
        var now = DateTime.UtcNow;
        // The rider's dot only while on the way, and only if their phone reported in the last 3 minutes.
        var rider = stage == "OnTheWay" && s.RiderSeenAt > now.AddMinutes(-3) ? Geo.Point(s.RiderLat, s.RiderLng) : null;
        var km = Geo.Between(shop, home);

        DateTime? eta = null;
        if (stage is "Delivered" or "Collected" or "Closed") eta = null;
        else if (!delivery) eta = readyAt ?? U(s.PromisedAt);
        else if (stage == "OnTheWay" && rider is not null && home is not null)
            eta = now.AddMinutes(Geo.RideMinutes(Geo.DistanceKm(rider.Lat, rider.Lng, home.Lat, home.Lng)));
        else if (stage == "OnTheWay") eta = U(s.PromisedAt) ?? U(s.DispatchedAt)?.AddMinutes(km is double k ? Geo.RideMinutes(k) : 20);
        else eta = U(s.PromisedAt) ?? readyAt?.AddMinutes(km is double k2 ? Geo.RideMinutes(k2) : 20);

        var riderFirst = (s.RiderName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return new TrackDto(s.TableCode, s.TableName, s.Channel.ToString(), stage, U(s.OpenedAt)!.Value, readyAt, U(s.PromisedAt), eta,
            U(s.DispatchedAt), U(s.DeliveredAt), riderFirst, shop, home, rider, rider is null ? null : U(s.RiderSeenAt), km);
    }

    // ---------- reception ----------

    /// <summary>The time told to the customer: at the door by (delivery) or ready by (takeaway).</summary>
    public async Task Promise(string code, int minutes, CancellationToken ct = default)
    {
        if (minutes < 5 || minutes > 240) throw new DomainException("Choose a time between 5 minutes and 4 hours.");
        var s = await OpenTicket(code, ct);
        s.PromisedAt = DateTime.UtcNow.AddMinutes(minutes);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>A private link for the rider's phone. Making a new one switches the old one off.</summary>
    public async Task<RiderLinkDto> RiderLink(string code, RiderLinkInput input, CancellationToken ct = default)
    {
        var s = await OpenTicket(code, ct);
        if (s.Channel != OrderChannel.Delivery) throw new DomainException("Only deliveries have a rider.");
        if (input.RiderName is not null) s.RiderName = input.RiderName.Trim();
        if (input.RiderPhone is not null) s.RiderPhone = LoginName.Normalize(input.RiderPhone);
        s.RiderKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        s.RiderLat = null; s.RiderLng = null; s.RiderSeenAt = null;
        await _db.SaveChangesAsync(ct);
        return new RiderLinkDto($"/r/{_tenant.Slug}/ride/{s.TableCode}?k={s.RiderKey}", s.RiderName, s.RiderPhone);
    }

    public async Task Dispatch(string code, CancellationToken ct = default) => await Leave(await OpenTicket(code, ct), ct);

    /// <summary>Delivered (delivery) or collected (takeaway). Anything still waiting to be handed over is.</summary>
    public async Task Delivered(string code, CancellationToken ct = default) => await Arrive(await OpenTicket(code, ct), ct);

    private async Task Leave(TableSession s, CancellationToken ct)
    {
        if (s.DeliveredAt is not null) throw new DomainException("This order has already been delivered.");
        var orders = await OrdersOf(s, ct);
        if (orders.Count == 0) throw new DomainException("There is nothing on this order.");
        if (orders.Any(o => o.Status is OrderStatus.Placed or OrderStatus.Approved or OrderStatus.Cooking))
            throw new DomainException("The food isn't ready yet. Wait for the kitchen.");
        s.DispatchedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        foreach (var o in orders.Where(o => o.Status == OrderStatus.Ready)) await _orders.Serve(o.Id, ct);
    }

    private async Task Arrive(TableSession s, CancellationToken ct)
    {
        if (s.DeliveredAt is not null) return;
        var orders = await OrdersOf(s, ct);
        if (orders.Any(o => o.Status is OrderStatus.Placed or OrderStatus.Approved or OrderStatus.Cooking))
            throw new DomainException("The food isn't ready yet. Wait for the kitchen.");
        var now = DateTime.UtcNow;
        if (s.Channel == OrderChannel.Delivery) s.DispatchedAt ??= now;
        s.DeliveredAt = now;
        await _db.SaveChangesAsync(ct);
        foreach (var o in orders.Where(o => o.Status == OrderStatus.Ready)) await _orders.Serve(o.Id, ct);
    }

    // ---------- the rider (holder of the link) ----------

    private async Task<TableSession> RiderTicket(string code, string? key, CancellationToken ct)
    {
        var s = await Session(code, ct);
        var good = s is not null && s.RiderKey.Length >= 20 && key is { Length: >= 20 }
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s.RiderKey), Encoding.UTF8.GetBytes(key))
            && (s.IsOpen || s.ClosedAt > DateTime.UtcNow.AddHours(-2));
        if (!good) throw new DomainException("This rider link isn't valid any more. Ask the restaurant for a new one.");
        return s!;
    }

    public async Task<RideDto> Ride(string code, string? key, decimal collect, CancellationToken ct = default)
    {
        var s = await RiderTicket(code, key, ct);
        var orders = await OrdersOf(s, ct);
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        var shop = Geo.Point(r.Latitude, r.Longitude);
        var home = Geo.Point(s.DeliveryLat, s.DeliveryLng);
        var items = orders.SelectMany(o => o.Items).GroupBy(i => i.Name).Select(g => $"{g.Sum(i => i.Qty)}× {g.Key}").ToList();
        var note = string.Join(" · ", orders.Select(o => o.CustomerNote).Where(n => !string.IsNullOrWhiteSpace(n)));
        var ready = orders.Count > 0 && orders.All(o => o.Status is OrderStatus.Ready or OrderStatus.Served or OrderStatus.Paid);
        return new RideDto(s.TableCode, s.TableName, r.Name, s.CustomerName, s.CustomerPhone, s.DeliveryAddress, shop, home,
            Geo.Between(shop, home), items, note, s.IsOpen ? collect : 0, U(s.PromisedAt), U(s.DispatchedAt), U(s.DeliveredAt), ready, StageOf(s, orders));
    }

    public async Task RiderAt(string code, string? key, RiderLocationInput at, CancellationToken ct = default)
    {
        if (!Geo.Valid(at.Lat, at.Lng)) throw new DomainException("That location doesn't look right.");
        var s = await RiderTicket(code, key, ct);
        if (s.DeliveredAt is not null || !s.IsOpen) return;
        s.RiderLat = at.Lat; s.RiderLng = at.Lng; s.RiderSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RiderPickup(string code, string? key, CancellationToken ct = default) => await Leave(await RiderTicket(code, key, ct), ct);
    public async Task RiderDelivered(string code, string? key, CancellationToken ct = default) => await Arrive(await RiderTicket(code, key, ct), ct);
}
