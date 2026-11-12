using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QrDine.Application.Admin;
using QrDine.Application.Auth;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Application.Customers;
using QrDine.Application.Expenses;
using QrDine.Application.Inventory;
using QrDine.Application.Loyalty;
using QrDine.Application.Menu;
using QrDine.Application.Menu.Dtos;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Printing;
using QrDine.Application.Reports;
using QrDine.Application.Staffing;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Inventory;
using QrDine.Domain.Menu;
using QrDine.Domain.Ordering;
using QrDine.Domain.Tables;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// The part of the system a restaurant can't afford to get wrong: which order belongs to which
/// table and whose bill it lands on. Runs the real services against a real (in-memory SQLite)
/// database, so the tenant filter and the database constraints are exercised too.
/// </summary>
public class FloorTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();
    private readonly RecordingNotifier _notifier = new();

    private int _biryani;
    private int _naan;

    public FloorTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant);
        _db.Database.EnsureCreated();
        Seed(1, "test").GetAwaiter().GetResult();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private OrderService Orders => new(_db, _tenant, _notifier, new NoPrinter(), new InventoryService(_db));
    private BillingService Billing => new(_db, _tenant, _notifier, new FiscalSyncService(_db, _tenant, new NoFiscal()),
        new NoPrinter(), new LoyaltyService(_db, _tenant), NullLogger<BillingService>.Instance);
    private TicketService Tickets => new(_db, Orders, _tenant);
    private DeliveryService Delivery_ => new(_db, Orders, _tenant);
    private CustomerService Customers => new(_db);
    private PayrollService Payroll => new(_db);
    private BusinessReportService Reports => new(_db, _tenant);

    /// <summary>Prices are tax-free and without service charge unless a test says otherwise, so the arithmetic is easy to read.</summary>
    private async Task Seed(int id, string slug)
    {
        _tenant.Set(id, slug);
        _db.Restaurants.Add(new Domain.Restaurants.Restaurant
        {
            Id = id, Name = "Test " + slug, Slug = slug, Currency = "PKR", TaxEnabled = false, TaxPercent = 0, ServiceChargePercent = 0,
        });
        await _db.SaveChangesAsync();

        var branch = new Domain.Restaurants.Branch { Name = "Main" };
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();
        foreach (var code in new[] { "T1", "T2", "T3" })
            _db.Tables.Add(new DiningTable { BranchId = branch.Id, Code = code, Name = "Table " + code[1..] });
        var category = new MenuCategory { Name = "Food" };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        var biryani = new MenuItem { CategoryId = category.Id, Name = "Biryani", Price = 500 };
        var naan = new MenuItem { CategoryId = category.Id, Name = "Naan", Price = 50 };
        _db.MenuItems.AddRange(biryani, naan);
        await _db.SaveChangesAsync();
        _biryani = biryani.Id;
        _naan = naan.Id;
    }

    private async Task Configure(Action<Domain.Restaurants.Restaurant> change)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == _tenant.RestaurantId);
        change(r);
        await _db.SaveChangesAsync();
    }

    private List<OrderLineRequest> Lines(int biryani = 1, int naan = 0)
    {
        var lines = new List<OrderLineRequest>();
        if (biryani > 0) lines.Add(new OrderLineRequest(_biryani, biryani, null));
        if (naan > 0) lines.Add(new OrderLineRequest(_naan, naan, null));
        return lines;
    }

    private Task<OrderDto> Dine(string table, int biryani = 1, int naan = 0) =>
        Orders.Place(new PlaceOrderRequest(table, Lines(biryani, naan), null), OrderSource.Waiter);

    private Task<TicketPlaced> Parcel(int biryani = 1, string? name = null, string? phone = null) =>
        Tickets.Place(new TicketRequest("Takeaway", name, phone, null, null, null, Lines(biryani), null));

    private Task<TicketPlaced> Delivery(decimal charge = 150, string phone = "03001234567") =>
        Tickets.Place(new TicketRequest("Delivery", "Ali", phone, "House 5, Gulberg", charge, null, Lines(), null));

    // ---- parcel and delivery ----

    [Fact]
    public async Task Two_parcels_never_share_a_ticket_or_a_bill()
    {
        var first = await Parcel(biryani: 1);
        var second = await Parcel(biryani: 2);

        Assert.NotEqual(first.Ticket.TableCode, second.Ticket.TableCode);
        Assert.Equal(500, (await Billing.GetBill(first.Ticket.TableCode)).Total);
        Assert.Equal(1000, (await Billing.GetBill(second.Ticket.TableCode)).Total);

        await Billing.Settle(first.Ticket.TableCode, new SettleRequest("Cash", 0));

        var left = await Billing.GetBill(second.Ticket.TableCode);
        Assert.True(left.HasOpenOrders);
        Assert.Equal(1000, left.Total);
        Assert.Single(await Tickets.Open());
    }

    [Fact]
    public async Task Tickets_count_up_like_a_token_book()
    {
        Assert.Equal("Parcel 1", (await Parcel()).Ticket.TableName);
        Assert.Equal("Parcel 2", (await Parcel()).Ticket.TableName);
        Assert.Equal("Delivery 1", (await Delivery()).Ticket.TableName);
    }

    [Fact]
    public async Task Service_charge_is_only_for_sitting_down()
    {
        await Configure(r => r.ServiceChargePercent = 10);
        await Dine("T1");
        var parcel = await Parcel();

        Assert.Equal(50, (await Billing.GetBill("T1")).ServiceChargeAmount);
        Assert.Equal(0, (await Billing.GetBill(parcel.Ticket.TableCode)).ServiceChargeAmount);
        var invoice = await Billing.Settle(parcel.Ticket.TableCode, new SettleRequest("Cash", 0));
        Assert.Equal(500, invoice.Total);
    }

    [Fact]
    public async Task Delivery_charge_is_on_the_bill_without_tax()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 10; });
        var ticket = await Delivery(charge: 150);

        var bill = await Billing.GetBill(ticket.Ticket.TableCode);
        Assert.Contains(bill.Lines, l => l.Name == "Delivery charge");
        Assert.Equal(50, bill.TaxAmount);
        Assert.Equal(500 + 50 + 150, bill.Total);
        Assert.Equal("Delivery", bill.Channel);
        Assert.Equal("House 5, Gulberg", bill.DeliveryAddress);
    }

    [Theory]
    [InlineData("", "House 5")]
    [InlineData("03001234567", "")]
    public async Task A_delivery_needs_a_phone_and_an_address(string phone, string address)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Tickets.Place(new TicketRequest("Delivery", "Ali", phone, address, 100, null, Lines(), null)));
        Assert.Contains("phone number and address", ex.Message);
        Assert.Empty(await _db.Tables.Where(t => t.Kind != OrderChannel.Dine).ToListAsync());
    }

    [Fact]
    public async Task A_ticket_that_cant_be_made_leaves_nothing_behind()
    {
        await Assert.ThrowsAsync<DomainException>(() => Tickets.Place(new TicketRequest("Takeaway", null, null, null, null, null,
            new List<OrderLineRequest> { new(999999, 1, null) }, null)));
        Assert.Empty(await _db.Tables.Where(t => t.Kind != OrderChannel.Dine).ToListAsync());
        Assert.Empty(await _db.Sessions.Where(s => s.Channel != OrderChannel.Dine).ToListAsync());
    }

    [Fact]
    public async Task Guests_cant_open_a_tickets_private_table()
    {
        var ticket = await Parcel();
        var auth = new AuthService(_db, _tenant, new NoTokens(), new NoHasher(), new PermissionService(_db));
        await Assert.ThrowsAsync<DomainException>(() => auth.IssueGuestSession(ticket.Ticket.TableCode));
        Assert.NotNull(await auth.IssueGuestSession("T1"));
    }

    // ---- takeaway and delivery from the guest's own phone (the restaurant's order QR) ----

    private Task<TicketPlaced> GuestOrder(string channel, string? name = "Sara", string? phone = "0300 1234567",
        string? address = "House 5, Gulberg") =>
        Tickets.PlaceGuest(new GuestTicketRequest(channel, name, phone, address, Lines(), null));

    [Fact]
    public async Task Guest_takeaway_and_delivery_are_off_until_the_restaurant_turns_them_on()
    {
        await Assert.ThrowsAsync<DomainException>(() => GuestOrder("Takeaway"));
        await Assert.ThrowsAsync<DomainException>(() => GuestOrder("Delivery"));
        await Assert.ThrowsAsync<DomainException>(() => GuestOrder("Dine"));
        Assert.Empty(await _db.Tables.Where(t => t.Kind != OrderChannel.Dine).ToListAsync());
    }

    [Fact]
    public async Task A_guest_delivery_waits_for_reception_and_pays_the_restaurants_delivery_charge()
    {
        await Configure(r => { r.GuestDelivery = true; r.DeliveryFee = 120; });
        var placed = await GuestOrder("Delivery");

        Assert.Equal("Delivery 1", placed.Ticket.TableName);
        Assert.Equal("03001234567", placed.Ticket.CustomerPhone);
        Assert.Equal(120, placed.Ticket.DeliveryCharge);
        Assert.Equal("Qr", placed.Order.Source);
        Assert.Equal("Placed", placed.Order.Status);             // reception approves it first, like a table order
        Assert.Equal(500 + 120, (await Billing.GetBill(placed.Ticket.TableCode)).Total);
        Assert.Contains(await Tickets.Open(), t => t.TableCode == placed.Ticket.TableCode);
    }

    [Fact]
    public async Task A_guest_takeaway_never_carries_an_address_or_a_delivery_charge()
    {
        await Configure(r => { r.GuestTakeaway = true; r.DeliveryFee = 120; });
        var placed = await GuestOrder("Takeaway");
        Assert.Equal("Parcel 1", placed.Ticket.TableName);
        Assert.Equal(0, placed.Ticket.DeliveryCharge);
        Assert.Equal("", placed.Ticket.Address);
        await Assert.ThrowsAsync<DomainException>(() => GuestOrder("Delivery"));   // only takeaway is on
    }

    [Theory]
    [InlineData("", "03001234567", "House 5, Gulberg")]
    [InlineData("Sara", "", "House 5, Gulberg")]
    [InlineData("Sara", "12345", "House 5, Gulberg")]
    [InlineData("Sara", "03001234567", "")]
    public async Task A_guest_delivery_needs_a_name_a_mobile_number_and_an_address(string name, string phone, string address)
    {
        await Configure(r => r.GuestDelivery = true);
        await Assert.ThrowsAsync<DomainException>(() => GuestOrder("Delivery", name, phone, address));
        Assert.Empty(await _db.Tables.Where(t => t.Kind != OrderChannel.Dine).ToListAsync());
    }

    [Fact]
    public async Task A_guest_never_changes_the_restaurants_customer_cards()
    {
        _db.Customers.Add(new Domain.Customers.Customer { Name = "Ahmed (regular)", Phone = "03001234567", Address = "Shop 2, Mall Road" });
        await _db.SaveChangesAsync();
        await Configure(r => r.GuestDelivery = true);

        await GuestOrder("Delivery", name: "Someone else", address: "Somewhere else");

        var card = await _db.Customers.SingleAsync();
        Assert.Equal("Ahmed (regular)", card.Name);
        Assert.Equal("Shop 2, Mall Road", card.Address);
    }

    [Fact]
    public async Task A_settled_ticket_cant_be_ordered_on_again()
    {
        await Configure(r => r.GuestTakeaway = true);
        var placed = await GuestOrder("Takeaway");
        await Orders.Approve(placed.Order.Id);
        await Billing.Settle(placed.Ticket.TableCode, new SettleRequest("Cash", 0));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Orders.Place(new PlaceOrderRequest(placed.Ticket.TableCode, Lines(), null), OrderSource.Qr));
        Assert.Contains("already closed", ex.Message);
    }

    [Fact]
    public async Task The_order_qr_needs_a_plan_with_ordering_and_billing()
    {
        await Configure(r => { r.GuestTakeaway = true; r.FeatureBilling = false; });
        var ex = await Assert.ThrowsAsync<DomainException>(() => GuestOrder("Takeaway"));
        Assert.Contains("plan", ex.Message);
        Assert.False((await new MenuService(_db).GetMenu("test"))!.Restaurant.Pickup!.Takeaway);
    }

    [Fact]
    public async Task The_guest_menu_says_what_the_order_qr_offers()
    {
        await Configure(r => { r.GuestTakeaway = true; r.DeliveryFee = 99; r.DeliveryNote = "Within 5 km"; });
        var menu = await new MenuService(_db).GetMenu("test");
        Assert.Equal(new PickupDto(true, false, 99, "Within 5 km"), menu!.Restaurant.Pickup);
    }

    // ---- delivery handling: map pin, promised time, rider link and trip, tracking ----

    private const double ShopLat = 31.5108, ShopLng = 74.3440;   // Liberty, Lahore
    private Task<TicketPlaced> PinnedDelivery(double lat, double lng) =>
        Tickets.PlaceGuest(new GuestTicketRequest("Delivery", "Sara", "03001234567", "House 12, Street 4, Gulberg III", Lines(), null, lat, lng));

    private async Task Cooked(TicketPlaced t) { await Orders.Approve(t.Order.Id); await Orders.Cook(t.Order.Id, 15); await Orders.Ready(t.Order.Id); }

    [Fact]
    public async Task A_pin_outside_the_delivery_area_is_refused_and_one_inside_is_kept()
    {
        await Configure(r => { r.GuestDelivery = true; r.Latitude = ShopLat; r.Longitude = ShopLng; r.DeliveryRadiusKm = 5; });
        var far = await Assert.ThrowsAsync<DomainException>(() => PinnedDelivery(31.4700, 74.4100));   // ~7.8 km
        Assert.Contains("delivers within 5 km", far.Message);

        var near = await PinnedDelivery(31.5260, 74.3570);                                             // ~2.1 km
        var row = (await Tickets.Open()).Single(t => t.TableCode == near.Ticket.TableCode);
        Assert.Equal(31.5260, row.Home!.Lat, 4);
        Assert.InRange(row.DistanceKm!.Value, 1.8, 2.5);
    }

    [Fact]
    public async Task A_rider_needs_the_link_and_cant_leave_before_the_food_is_ready()
    {
        await Configure(r => r.GuestDelivery = true);
        var t = await PinnedDelivery(31.5260, 74.3570);
        var link = await Delivery_.RiderLink(t.Ticket.TableCode, new RiderLinkInput("Imran Khan", "0321 1112233"));
        var key = link.Path.Split("k=")[1];

        await Assert.ThrowsAsync<DomainException>(() => Delivery_.Ride(t.Ticket.TableCode, "wrong-key-wrong-key-xx", 0));
        await Assert.ThrowsAsync<DomainException>(() => Delivery_.Ride(t.Ticket.TableCode, null, 0));
        var ride = await Delivery_.Ride(t.Ticket.TableCode, key, 500);
        Assert.Equal("Sara", ride.CustomerName);
        Assert.False(ride.FoodReady);
        await Assert.ThrowsAsync<DomainException>(() => Delivery_.RiderPickup(t.Ticket.TableCode, key));

        // a new link switches the old one off
        var again = await Delivery_.RiderLink(t.Ticket.TableCode, new RiderLinkInput(null, null));
        await Assert.ThrowsAsync<DomainException>(() => Delivery_.Ride(t.Ticket.TableCode, key, 0));
        Assert.NotNull(await Delivery_.Ride(t.Ticket.TableCode, again.Path.Split("k=")[1], 0));
    }

    [Fact]
    public async Task The_guest_follows_a_delivery_from_kitchen_to_door()
    {
        await Configure(r => { r.GuestDelivery = true; r.Latitude = ShopLat; r.Longitude = ShopLng; });
        var t = await PinnedDelivery(31.5260, 74.3570);
        var code = t.Ticket.TableCode;
        Assert.Equal("Received", (await Delivery_.Track(code)).Stage);

        await Delivery_.Promise(code, 40);
        await Orders.Approve(t.Order.Id); await Orders.Cook(t.Order.Id, 15);
        var cooking = await Delivery_.Track(code);
        Assert.Equal("Cooking", cooking.Stage);
        Assert.NotNull(cooking.ReadyAt);
        Assert.InRange((cooking.EtaAt!.Value - DateTime.UtcNow).TotalMinutes, 38, 41);   // the promised time wins

        await Orders.Ready(t.Order.Id);
        var key = (await Delivery_.RiderLink(code, new RiderLinkInput("Imran", null))).Path.Split("k=")[1];
        await Delivery_.RiderPickup(code, key);
        Assert.Equal("Served", (await Orders.GetOrder(t.Order.Id)).Status);                   // out for delivery
        await Delivery_.RiderAt(code, key, new RiderLocationInput(31.5200, 74.3520));
        var onTheWay = await Delivery_.Track(code);
        Assert.Equal("OnTheWay", onTheWay.Stage);
        Assert.Equal("Imran", onTheWay.RiderName);
        Assert.NotNull(onTheWay.Rider);
        Assert.InRange((onTheWay.EtaAt!.Value - DateTime.UtcNow).TotalMinutes, 4, 10);       // ~1 km left

        await Delivery_.RiderDelivered(code, key);
        var done = await Delivery_.Track(code);
        Assert.Equal("Delivered", done.Stage);
        Assert.NotNull(done.DeliveredAt);
        Assert.Null(done.Rider);
        Assert.Null(done.EtaAt);
    }

    [Fact]
    public async Task A_takeaway_shows_the_kitchens_ready_time_and_is_collected_at_the_counter()
    {
        await Configure(r => r.GuestTakeaway = true);
        var t = await GuestOrder("Takeaway");
        await Orders.Approve(t.Order.Id); await Orders.Cook(t.Order.Id, 12);
        var track = await Delivery_.Track(t.Ticket.TableCode);
        Assert.Equal(track.ReadyAt, track.EtaAt);
        Assert.InRange((track.EtaAt!.Value - DateTime.UtcNow).TotalMinutes, 11, 12.1);
        await Assert.ThrowsAsync<DomainException>(() => Delivery_.Delivered(t.Ticket.TableCode));   // not ready yet
        await Orders.Ready(t.Order.Id);
        await Delivery_.Delivered(t.Ticket.TableCode);
        Assert.Equal("Collected", (await Delivery_.Track(t.Ticket.TableCode)).Stage);
        await Assert.ThrowsAsync<DomainException>(() => Delivery_.RiderLink(t.Ticket.TableCode, new RiderLinkInput("x", null)));
    }

    [Fact]
    public async Task Ticket_tables_stay_out_of_every_table_list()
    {
        await Parcel();
        await Delivery();
        Assert.Equal(3, (await new MenuService(_db).GetTables()).Count);
        Assert.Equal(3, (await new RestaurantAdminService(_db, new NoHasher(), _tenant).ListTables()).Count);
    }

    [Fact]
    public async Task A_delivery_remembers_the_customer_for_next_time()
    {
        await Delivery(phone: "0300 1234567");
        await Delivery(phone: "+92 300 1234567");
        var customer = await _db.Customers.SingleAsync();
        Assert.Equal("03001234567", customer.Phone);
        Assert.Equal("House 5, Gulberg", customer.Address);
    }

    // ---- shifting tables ----

    [Fact]
    public async Task Moving_to_a_free_table_takes_the_bill_and_nothing_else()
    {
        var order = await Dine("T1", biryani: 2);
        var other = await Dine("T3", naan: 1, biryani: 0);

        var moved = await Orders.MoveTable("T1", "T2");

        Assert.Equal("T2", Assert.Single(moved).TableCode);
        Assert.Equal(order.Id, moved[0].Id);
        Assert.Equal(1000, (await Billing.GetBill("T2")).Total);
        Assert.False((await Billing.GetBill("T1")).HasOpenOrders);
        Assert.Equal(50, (await Billing.GetBill("T3")).Total);
        Assert.Equal(other.Id, (await Orders.TableOrders("T3")).Single().Id);
    }

    [Fact]
    public async Task The_old_table_is_free_for_new_guests_after_a_move()
    {
        await Dine("T1", biryani: 2);
        await Orders.MoveTable("T1", "T2");
        await Dine("T1", biryani: 1);

        Assert.Equal(500, (await Billing.GetBill("T1")).Total);
        Assert.Equal(1000, (await Billing.GetBill("T2")).Total);
    }

    [Fact]
    public async Task Phones_on_the_old_table_hear_about_the_move()
    {
        await Dine("T1");
        await Orders.MoveTable("T1", "T2");
        Assert.Contains("T1", _notifier.MovedFrom);
    }

    [Fact]
    public async Task Merging_joins_the_bills_and_keeps_what_was_paid()
    {
        await Dine("T1", biryani: 2);
        await Dine("T2", biryani: 1);
        await Billing.PayPartial("T1", new PayPartRequest(300, "Cash"));

        await Orders.MoveTable("T1", "T2");

        var bill = await Billing.GetBill("T2");
        Assert.Equal(1500, bill.Total);
        Assert.Equal(300, bill.AmountPaid);
        Assert.Equal(1200, bill.Remaining);
        Assert.False((await Billing.GetBill("T1")).HasOpenOrders);
        Assert.Equal(1, await _db.Sessions.CountAsync(s => s.IsOpen));

        var settled = await Billing.PayPartial("T2", new PayPartRequest(1200, "Card"));
        Assert.True(settled.Settled);
        Assert.Equal(1500, settled.Invoice!.Total);
    }

    [Fact]
    public async Task A_moved_order_keeps_its_place_in_the_kitchen()
    {
        var order = await Dine("T1");
        await Orders.Approve(order.Id);
        await Orders.Cook(order.Id, 10);

        var moved = await Orders.MoveTable("T1", "T2");

        Assert.Equal("Cooking", moved.Single().Status);
        Assert.Equal("Ready", (await Orders.Ready(order.Id)).Status);
    }

    [Fact]
    public async Task Cancelling_the_last_order_on_a_ticket_closes_it_but_a_table_stays_open()
    {
        await Configure(r => { r.GuestDelivery = true; });
        var placed = await GuestOrder("Delivery");
        await Orders.Cancel(placed.Order.Id, "customer changed their mind");
        Assert.DoesNotContain(await Tickets.Open(), t => t.TableCode == placed.Ticket.TableCode);
        await Assert.ThrowsAsync<DomainException>(() => Orders.Place(
            new PlaceOrderRequest(placed.Ticket.TableCode, Lines(), null), OrderSource.Qr, null));

        var dine = await Dine("T1");
        await Orders.Cancel(dine.Id, null);
        Assert.True(await _db.Sessions.AnyAsync(s => s.TableCode == "T1" && s.IsOpen));
    }

    [Fact]
    public async Task Paid_and_cancelled_orders_stay_with_the_table_they_were_on()
    {
        var kept = await Dine("T1");
        var cancelled = await Dine("T1");
        await Orders.Cancel(cancelled.Id, null);

        await Orders.MoveTable("T1", "T2");

        var row = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == cancelled.Id);
        Assert.Equal("T1", row.TableCode);
        Assert.Equal("T2", (await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == kept.Id)).TableCode);
    }

    [Fact]
    public async Task Tickets_cant_be_moved_or_moved_onto()
    {
        var parcel = await Parcel();
        await Dine("T1");
        await Assert.ThrowsAsync<DomainException>(() => Orders.MoveTable(parcel.Ticket.TableCode, "T2"));
        await Assert.ThrowsAsync<DomainException>(() => Orders.MoveTable("T1", parcel.Ticket.TableCode));
        await Assert.ThrowsAsync<DomainException>(() => Orders.MoveTable("T1", "T1"));
        await Assert.ThrowsAsync<DomainException>(() => Orders.MoveTable("T2", "T3"));
    }

    // ---- taking a dish off ----

    [Fact]
    public async Task Removing_a_dish_lowers_the_bill_and_keeps_a_record()
    {
        var order = await Dine("T1", biryani: 2, naan: 1);
        var line = order.Items.Single(i => i.Name == "Biryani");

        var after = await Orders.VoidItem(order.Id, line.Id, new VoidItemRequest(1, "Guest changed mind"), "cashier1");

        Assert.Equal(1, after.Items.Single(i => i.Name == "Biryani").Qty);
        Assert.Equal(550, (await Billing.GetBill("T1")).Total);
        var record = await _db.OrderVoids.SingleAsync();
        Assert.Equal("Guest changed mind", record.Reason);
        Assert.Equal("cashier1", record.VoidedBy);
        Assert.Equal(500, record.UnitPrice);
    }

    [Fact]
    public async Task The_last_dish_can_only_go_by_cancelling()
    {
        var order = await Dine("T1");
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Orders.VoidItem(order.Id, order.Items[0].Id, new VoidItemRequest(1, "Mistake"), "x"));
        Assert.Contains("Cancel the order", ex.Message);
        Assert.Equal(500, (await Billing.GetBill("T1")).Total);
    }

    [Fact]
    public async Task A_removal_needs_a_reason_and_a_real_quantity()
    {
        var order = await Dine("T1", biryani: 2, naan: 1);
        var line = order.Items.Single(i => i.Name == "Biryani");
        await Assert.ThrowsAsync<DomainException>(() => Orders.VoidItem(order.Id, line.Id, new VoidItemRequest(1, "  "), "x"));
        await Assert.ThrowsAsync<DomainException>(() => Orders.VoidItem(order.Id, line.Id, new VoidItemRequest(3, "Mistake"), "x"));
    }

    [Fact]
    public async Task Ingredients_of_a_removed_dish_go_back_to_stock()
    {
        await Configure(r => r.StockTrackingEnabled = true);
        var chicken = new StockItem { Name = "Chicken", Unit = "kg", Quantity = 10 };
        _db.Stock.Add(chicken);
        await _db.SaveChangesAsync();
        await new InventoryService(_db).SetRecipe(_biryani, new RecipeInput(new List<RecipeLineInput> { new(chicken.Id, 0.5m) }));

        var order = await Dine("T1", biryani: 2, naan: 1);
        await Orders.Approve(order.Id);
        Assert.Equal(9, (await _db.Stock.AsNoTracking().SingleAsync()).Quantity);

        await Orders.VoidItem(order.Id, order.Items.Single(i => i.Name == "Biryani").Id, new VoidItemRequest(1, "Burnt"), "x");
        Assert.Equal(9.5m, (await _db.Stock.AsNoTracking().SingleAsync()).Quantity);
    }

    // ---- guests calling staff ----

    [Fact]
    public async Task Tapping_call_waiter_twice_calls_once()
    {
        var svc = new ServiceRequestService(_db, _tenant, _notifier);
        var a = await svc.Call("T1", "Waiter");
        var b = await svc.Call("T1", "waiter");
        Assert.Equal(a.Id, b.Id);
        await svc.Call("T1", "Bill");
        Assert.Equal(2, (await svc.Open()).Count);

        await svc.Done(a.Id, "w1");
        Assert.Single(await svc.Open());
        Assert.NotEqual(a.Id, (await svc.Call("T1", "Waiter")).Id);
    }

    // ---- customer khata ----

    [Fact]
    public async Task A_khata_bill_lands_on_the_customers_ledger()
    {
        var customer = await Customers.Create(new CustomerInput("Office", "03001112222", null, null, 0, 0));
        await Dine("T1", biryani: 2);

        var invoice = await Billing.Settle("T1", new SettleRequest("Khata", 0, 0, customer));

        Assert.Equal("Khata", invoice.PaymentMethod);
        var ledger = await Customers.Ledger(customer);
        var row = Assert.Single(ledger.Rows);
        Assert.Equal(1000, row.Charged);
        Assert.Equal(invoice.Id, row.InvoiceId);
        Assert.Equal(1000, ledger.Customer.Balance);

        await Customers.AddPayment(customer, new CustomerPaymentInput(null, 400, "Cash", null, null), "c1");
        Assert.Equal(600, (await Customers.Ledger(customer)).Customer.Balance);
    }

    [Fact]
    public async Task Only_the_unpaid_part_goes_on_khata()
    {
        var customer = await Customers.Create(new CustomerInput("Office", null, null, null, 0, 0));
        await Dine("T1", biryani: 2);
        await Billing.PayPartial("T1", new PayPartRequest(300, "Cash"));

        await Billing.Settle("T1", new SettleRequest("Khata", 0, 0, customer));

        Assert.Equal(700, (await Customers.Ledger(customer)).Customer.Balance);
    }

    [Fact]
    public async Task Khata_limit_refuses_and_leaves_the_bill_open()
    {
        var customer = await Customers.Create(new CustomerInput("Office", null, null, null, 200, 1000));
        await Dine("T1", biryani: 2);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Billing.Settle("T1", new SettleRequest("Khata", 0, 0, customer)));
        Assert.Contains("limit", ex.Message);
        Assert.True((await Billing.GetBill("T1")).HasOpenOrders);
        Assert.Empty(await _db.CustomerEntries.ToListAsync());
    }

    [Fact]
    public async Task Khata_needs_a_customer_and_cant_be_a_split_part()
    {
        await Dine("T1");
        await Assert.ThrowsAsync<DomainException>(() => Billing.Settle("T1", new SettleRequest("Khata", 0)));
        await Assert.ThrowsAsync<DomainException>(() => Billing.PayPartial("T1", new PayPartRequest(100, "Khata")));
    }

    [Fact]
    public async Task A_customer_who_owes_cant_be_removed()
    {
        var customer = await Customers.Create(new CustomerInput("Office", null, null, null, 500, 0));
        await Assert.ThrowsAsync<DomainException>(() => Customers.Archive(customer));
    }

    // ---- salaries ----

    [Fact]
    public async Task Paying_a_salary_is_an_expense_and_taking_it_back_removes_it()
    {
        var person = await Payroll.Create(new EmployeeInput("Hamid", null, "Cook", 30000, null, null));
        var advance = await Payroll.AddEntry(person, new PayrollEntryInput("Advance", 5000, "2026-11", "2026-11-05", "Cash", null), "owner");
        await Payroll.AddEntry(person, new PayrollEntryInput("Deduction", 1000, "2026-11", "2026-11-06", null, "Late"), "owner");

        var row = (await Payroll.Month("2026-11")).Rows.Single();
        Assert.Equal(24000, row.Due);
        var expense = await _db.Expenses.SingleAsync();
        Assert.Equal("Salaries", expense.Category);
        Assert.Equal("Hamid", expense.PaidTo);

        var expenses = new ExpenseService(_db);
        var ex = await Assert.ThrowsAsync<DomainException>(() => expenses.Delete(expense.Id));
        Assert.Contains("salary payment", ex.Message);

        await Payroll.DeleteEntry(advance);
        Assert.Empty(await _db.Expenses.ToListAsync());
        Assert.Equal(29000, (await Payroll.Month("2026-11")).Rows.Single().Due);
    }

    [Fact]
    public async Task Attendance_is_one_mark_a_day_and_can_be_cleared()
    {
        var person = await Payroll.Create(new EmployeeInput("Hamid", null, null, 30000, null, null));
        await Payroll.SetAttendance(new AttendanceInput(person, "2026-11-05", "Present"));
        await Payroll.SetAttendance(new AttendanceInput(person, "2026-11-05", "Absent"));
        Assert.Equal("Absent", (await Payroll.Attendance("2026-11")).Single().Status);
        Assert.Equal(1, (await Payroll.Month("2026-11")).Rows.Single().Absent);

        await Payroll.SetAttendance(new AttendanceInput(person, "2026-11-05", ""));
        Assert.Empty(await Payroll.Attendance("2026-11"));
    }

    // ---- the till ----

    [Fact]
    public async Task Day_close_counts_cash_only_and_carries_to_tomorrow()
    {
        await Dine("T1", biryani: 2);
        await Billing.Settle("T1", new SettleRequest("Cash", 0));
        await Dine("T2", biryani: 1);
        await Billing.Settle("T2", new SettleRequest("Card", 0));
        var customer = await Customers.Create(new CustomerInput("Office", null, null, null, 300, 0));
        await Customers.AddPayment(customer, new CustomerPaymentInput(null, 200, "Cash", null, null), "c");
        await new ExpenseService(_db).Create(new ExpenseInput(null, "Gas", 100, "Cash", null, null, null), "c");

        var svc = new DayCloseService(_db, Reports);
        var view = await svc.View(null, null);
        Assert.Equal(1000 + 200 - 100, view.Expected);

        var closed = await svc.Close(new DayCloseInput(null, 500, 1550, 1000, "Sent home", null), "owner");
        Assert.Equal(1600, closed.Expected);
        Assert.Equal(-50, closed.Closed!.Difference);

        var tomorrow = BusinessDay.Today.AddDays(1).ToString("yyyy-MM-dd");
        Assert.Equal(550, (await svc.View(tomorrow, null)).OpeningCash);
    }

    [Fact]
    public async Task Counting_the_same_day_twice_replaces_the_count()
    {
        var svc = new DayCloseService(_db, Reports);
        await svc.Close(new DayCloseInput(null, 100, 100, 0, null, null), "a");
        await svc.Close(new DayCloseInput(null, 100, 120, 0, null, null), "b");
        var row = await _db.DayClosings.SingleAsync();
        Assert.Equal(120, row.Counted);
        Assert.Equal("b", row.ClosedBy);
    }

    // ---- bills ----

    [Fact]
    public async Task A_split_bill_with_a_discount_settles_at_the_discounted_total()
    {
        await Dine("T1", biryani: 2);
        var first = await Billing.PayPartial("T1", new PayPartRequest(400, "Cash", 100));
        Assert.False(first.Settled);
        Assert.Equal(500, first.Remaining);

        var last = await Billing.PayPartial("T1", new PayPartRequest(500, "Card", 100));
        Assert.True(last.Settled);
        Assert.Equal(900, last.Invoice!.Total);
        Assert.Equal(100, last.Invoice.DiscountAmount);
    }

    [Fact]
    public async Task Card_payments_can_carry_a_lower_tax_rate()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; r.TaxPercentCard = 5; });
        await Dine("T1", biryani: 2);
        Assert.Equal(160, (await Billing.GetBill("T1")).TaxAmount);
        Assert.Equal(50, (await Billing.GetBill("T1", null, default, "Card")).TaxAmount);
        Assert.Equal(160, (await Billing.GetBill("T1", null, default, "Cash")).TaxAmount);

        var invoice = await Billing.Settle("T1", new SettleRequest("JazzCash", 0));
        Assert.Equal(1050, invoice.Total);
    }

    [Fact]
    public async Task One_rate_for_everyone_when_no_card_rate_is_set()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; });
        await Dine("T1", biryani: 2);
        Assert.Equal(160, (await Billing.GetBill("T1", null, default, "Card")).TaxAmount);
    }

    [Fact]
    public async Task Without_permission_the_discount_is_the_owners_standing_one()
    {
        await Configure(r => { r.DiscountEnabled = true; r.DiscountIsPercent = true; r.DiscountValue = 10; });
        await Dine("T1", biryani: 2);

        Assert.Equal(100, (await Billing.GetBill("T1", 700, default, null, canDiscount: false)).DiscountAmount);
        var invoice = await Billing.Settle("T1", new SettleRequest("Cash", 700), null, default, canDiscount: false);
        Assert.Equal(900, invoice.Total);
    }

    [Fact]
    public async Task With_permission_the_cashier_can_set_the_discount()
    {
        await Dine("T1", biryani: 2);
        var invoice = await Billing.Settle("T1", new SettleRequest("Cash", 200));
        Assert.Equal(800, invoice.Total);
    }

    // ---- reports ----

    [Fact]
    public async Task Reports_separate_channels_and_leave_delivery_out_of_dish_sales()
    {
        var ticket = await Delivery(charge: 150);
        await Tickets.Update(ticket.Ticket.TableCode, new TicketUpdate(null, null, null, null, "Rider Bilal"));
        await Billing.Settle(ticket.Ticket.TableCode, new SettleRequest("Cash", 0));
        var parcel = await Parcel();
        await Billing.Settle(parcel.Ticket.TableCode, new SettleRequest("Cash", 0));
        await Dine("T1");
        await Billing.Settle("T1", new SettleRequest("Cash", 0));

        var report = await Reports.Build(null, null, null);

        Assert.Equal(3, report.Summary.ItemsSold);
        Assert.DoesNotContain(report.Items, i => i.Name == "Delivery charge");
        Assert.Equal(150, report.DeliveryCharges);
        Assert.Equal(650, report.Channels.Single(c => c.Name == "Delivery").Amount);
        Assert.Equal(500, report.Channels.Single(c => c.Name == "Parcel").Amount);
        Assert.Equal(500, report.Channels.Single(c => c.Name == "Dine-in").Amount);
        Assert.Equal("Rider Bilal", report.Riders.Single().Name);
    }

    [Fact]
    public async Task Reports_count_split_parts_by_how_each_was_paid()
    {
        await Dine("T1", biryani: 2);
        await Billing.PayPartial("T1", new PayPartRequest(300, "Cash"));
        await Billing.PayPartial("T1", new PayPartRequest(700, "Card"));

        var report = await Reports.Build(null, null, null);
        Assert.Equal(300, report.Closing.CashSales);
        Assert.Equal(700, report.Closing.OtherSales);
    }

    [Fact]
    public async Task A_bill_part_paid_then_settled_says_how_it_was_paid()
    {
        await Dine("T1", biryani: 2);
        await Billing.PayPartial("T1", new PayPartRequest(300, "Cash"));

        var invoice = await Billing.Settle("T1", new SettleRequest("Card", 0));

        Assert.Equal("Split (Cash+Card)", invoice.PaymentMethod);
        Assert.Equal(1000, invoice.Total);
        var report = await Reports.Build(null, null, null);
        Assert.Equal(300, report.Closing.CashSales);
        Assert.Equal(700, report.Closing.OtherSales);
    }

    [Fact]
    public async Task Cash_settled_after_a_card_part_reaches_the_drawer()
    {
        await Dine("T1", biryani: 2);
        await Billing.PayPartial("T1", new PayPartRequest(300, "Card"));

        await Billing.Settle("T1", new SettleRequest("Cash", 0, 700));

        var report = await Reports.Build(null, null, null);
        Assert.Equal(700, report.Closing.CashSales);
        Assert.Equal(300, report.Closing.OtherSales);
    }

    [Fact]
    public async Task Parts_paid_the_same_way_stay_a_plain_bill()
    {
        await Dine("T1", biryani: 2);
        await Billing.PayPartial("T1", new PayPartRequest(300, "Cash"));

        var invoice = await Billing.Settle("T1", new SettleRequest("Cash", 0, 700));

        Assert.Equal("Cash", invoice.PaymentMethod);
        Assert.Equal(1000, (await Reports.Build(null, null, null)).Closing.CashSales);
    }

    [Fact]
    public async Task Khata_sales_are_not_cash_until_the_money_comes_back()
    {
        var customer = await Customers.Create(new CustomerInput("Office", null, null, null, 0, 0));
        await Dine("T1", biryani: 2);
        await Billing.Settle("T1", new SettleRequest("Khata", 0, 0, customer));

        var before = await Reports.Build(null, null, null);
        Assert.Equal(0, before.Closing.CashSales);
        Assert.Equal(1000, before.Closing.KhataSales);
        Assert.Equal(0, before.Closing.CashInHand);
        Assert.Equal(1000, before.CustomerBalance);

        await Customers.AddPayment(customer, new CustomerPaymentInput(null, 1000, "Cash", null, null), "c");
        var after = await Reports.Build(null, null, null);
        Assert.Equal(1000, after.Closing.CashInHand);
        Assert.Equal(0, after.CustomerBalance);
    }

    [Fact]
    public async Task Removed_dishes_show_in_the_report()
    {
        var order = await Dine("T1", biryani: 2);
        await Orders.VoidItem(order.Id, order.Items[0].Id, new VoidItemRequest(1, "Burnt"), "c1");
        var report = await Reports.Build(null, null, null);
        var row = Assert.Single(report.Voids);
        Assert.Equal(500, row.Value);
        Assert.Equal("Burnt", row.Reason);
    }

    // ---- menu ----

    [Fact]
    public async Task Bulk_price_change_rounds_and_leaves_deals_alone()
    {
        var item = await _db.MenuItems.FirstAsync(m => m.Id == _biryani);
        item.DealPrice = 400;
        await _db.SaveChangesAsync();

        var changed = await new MenuAdminService(_db).BulkPrice(new Application.Menu.Dtos.BulkPriceRequest(7, 10, null));

        Assert.Equal(2, changed);
        var after = await _db.MenuItems.AsNoTracking().FirstAsync(m => m.Id == _biryani);
        Assert.Equal(540, after.Price);
        Assert.Equal(400, after.DealPrice);
    }

    // ---- restaurants can't see each other ----

    [Fact]
    public async Task One_restaurants_floor_is_invisible_to_another()
    {
        var customer = await Customers.Create(new CustomerInput("Office", null, null, null, 500, 0));
        await Parcel();
        await new ServiceRequestService(_db, _tenant, _notifier).Call("T1", "Waiter");
        await Payroll.Create(new EmployeeInput("Hamid", null, null, 30000, null, null));

        await Seed(2, "other");

        Assert.Empty(await Customers.List());
        Assert.Empty(await Tickets.Open());
        Assert.Empty(await new ServiceRequestService(_db, _tenant, _notifier).Open());
        Assert.Empty((await Payroll.Month(null)).Rows);
        await Assert.ThrowsAsync<DomainException>(() => Customers.Ledger(customer));

        // The other restaurant's ticket numbers start again from 1.
        Assert.Equal("Parcel 1", (await Parcel()).Ticket.TableName);
    }

    // ---- stand-ins for what the services talk to ----

    private sealed class RecordingNotifier : IOrderNotifier
    {
        public List<string> MovedFrom { get; } = new();
        public Task OrderChanged(string slug, OrderDto order, CancellationToken ct = default) => Task.CompletedTask;
        public Task OrderMoved(string slug, string fromTableCode, OrderDto order, CancellationToken ct = default)
        {
            MovedFrom.Add(fromTableCode);
            return Task.CompletedTask;
        }
        public Task ServiceRequestChanged(string slug, ServiceRequestDto request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoPrinter : IPrintDispatcher
    {
        public Task Dispatch(string slug, PrintJob job, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoFiscal : IFiscalService
    {
        public Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default) =>
            Task.FromResult(new FiscalResult("None", FiscalStatus.NotRequired, null, null, null));
    }

    private sealed class NoHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string hash) => password == hash;
    }

    private sealed class NoTokens : ITokenService
    {
        public string CreateStaffToken(StaffMember user, string slug, IEnumerable<string> permissions) => "staff";
        public string CreateGuestToken(int restaurantId, string slug, string tableCode) => "guest";
        public string CreatePlatformToken(PlatformAdmin admin) => "platform";
    }
}
