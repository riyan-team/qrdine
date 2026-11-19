using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Admin;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Application.Inventory;
using QrDine.Application.Menu;
using QrDine.Application.Menu.Dtos;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Platform;
using QrDine.Application.Printing;
using QrDine.Application.Purchasing;
using QrDine.Application.Validation;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Menu;
using QrDine.Domain.Tables;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// What every form is allowed to send. A wrong number is refused with a short reason the
/// screen can show, never quietly changed, and the same rule holds whether the check runs in
/// the form filter or in the service behind it.
/// </summary>
public class FormRulesTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();
    private int _category;

    public FormRulesTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant);
        _db.Database.EnsureCreated();
        Seed().GetAwaiter().GetResult();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private async Task Seed()
    {
        _tenant.Set(1, "test");
        _db.Restaurants.Add(new Domain.Restaurants.Restaurant { Id = 1, Name = "Test", Slug = "test", Currency = "PKR", TaxPercent = 16 });
        _db.Restaurants.Add(new Domain.Restaurants.Restaurant { Id = 2, Name = "Other", Slug = "other", Currency = "PKR" });
        await _db.SaveChangesAsync();
        var branch = new Domain.Restaurants.Branch { Name = "Main" };
        _db.Branches.Add(branch);
        var category = new MenuCategory { Name = "Food" };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        _category = category.Id;
        _db.Tables.Add(new DiningTable { BranchId = branch.Id, Code = "T1", Name = "Table 1" });
        await _db.SaveChangesAsync();
    }

    private RestaurantAdminService Admin => new(_db, new PlainHasher(), _tenant);
    private MenuAdminService Menu => new(_db);
    private TicketService Tickets => new(_db, new OrderService(_db, _tenant, new QuietNotifier(), new NoPrinter(), new InventoryService(_db)), _tenant);

    private static string? FirstError<T>(FluentValidation.IValidator<T> v, T input) =>
        v.Validate(input).Errors.FirstOrDefault()?.ErrorMessage;

    // ---- phone numbers ----
    [Theory]
    [InlineData("03001234567", true)]
    [InlineData("0300 1234567", true)]
    [InlineData("+92 300 1234567", true)]
    [InlineData("021-1234567", true)]
    [InlineData("(042) 1234567", true)]
    [InlineData("12345", false)]          // too short
    [InlineData("0300123456789012", false)] // 16 digits
    [InlineData("Ali", false)]
    [InlineData("0300-CALL-ME", false)]
    [InlineData("", false)]
    public void A_phone_number_has_7_to_15_digits(string typed, bool ok) => Assert.Equal(ok, Phone.IsValid(typed));

    [Fact]
    public void Optional_phones_may_be_blank_but_not_wrong()
    {
        Assert.Null(FirstError(new SupplierInputValidator(), new SupplierInput("Bilal", "", null, 0)));
        Assert.Equal(Phone.Message, FirstError(new SupplierInputValidator(), new SupplierInput("Bilal", "abc", null, 0)));
        Assert.Equal(Phone.Message, FirstError(new CustomerInputValidator(), new Application.Customers.CustomerInput("Ali", "123", null, null, 0, 0)));
        Assert.Equal(Phone.Message, FirstError(new RiderLinkInputValidator(), new RiderLinkInput("Rider", "call me")));
        Assert.Null(FirstError(new RiderLinkInputValidator(), new RiderLinkInput("Rider", null)));
    }

    // ---- tables and staff ----
    [Theory]
    [InlineData("T1", true)]
    [InlineData("roof-2", true)]
    [InlineData("Table 1", false)]
    [InlineData("T/1", false)]
    public void A_table_code_is_safe_for_the_qr_link(string code, bool ok) =>
        Assert.Equal(ok, new TableInputValidator().Validate(new TableInput(code, "Table", 1)).IsValid);

    [Theory]
    [InlineData("ali.khan", true)]
    [InlineData("03001234567", true)]
    [InlineData("0300 1234567", true)]
    [InlineData("+92 300 1234567", true)]
    [InlineData("ali khan", false)]
    public void Staff_may_sign_in_with_a_mobile_number_or_a_short_name(string username, bool ok)
    {
        Assert.Equal(ok, new StaffInputValidator().Validate(new StaffInput(username, "Ali", "Waiter", "secret1")).IsValid);
        Assert.Equal(ok, new StaffUpdateInputValidator().Validate(new StaffUpdateInput("Ali", "Waiter", Username: username)).IsValid);
    }

    [Fact]
    public async Task Renaming_staff_uses_one_spelling_and_checks_every_restaurant()
    {
        _tenant.Set(2, "other");
        await Admin.AddStaff(new StaffInput("03007654321", "Taken", "Waiter", "secret1"), true);
        _tenant.Set(1, "test");
        var id = await Admin.AddStaff(new StaffInput("bilal", "Bilal", "Waiter", "secret1"), true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Admin.UpdateStaff(id, new StaffUpdateInput("Bilal", "Waiter", Username: "0300 765 4321"), true));
        Assert.Contains("already in use", ex.Message);

        await Admin.UpdateStaff(id, new StaffUpdateInput("Bilal", "Waiter", Username: "+92 300 1112223"), true);
        Assert.Equal("03001112223", (await _db.Staff.FirstAsync(s => s.Id == id)).Username);
    }

    // ---- bill settings: refused, never clamped ----
    private static BillingSettingsInput Bill(decimal tax = 16, decimal service = 5, decimal discount = 0, bool discountIsPercent = false, decimal? card = null) =>
        new(true, "GST", tax, service, true, discountIsPercent, discount, true, true, card);

    [Theory]
    [InlineData(101, 5, 0, false, null, "Tax % must be between 0 and 100.")]
    [InlineData(-1, 5, 0, false, null, "Tax % must be between 0 and 100.")]
    [InlineData(16, 150, 0, false, null, "Service charge % must be between 0 and 100.")]
    [InlineData(16, 5, 0, false, 120.0, "Card tax % must be between 0 and 100.")]
    [InlineData(16, 5, 101, true, null, "Discount % must be between 0 and 100.")]
    [InlineData(16, 5, -5, false, null, "Discount can't be negative.")]
    public async Task Wrong_bill_settings_are_refused_with_a_reason(double tax, double service, double discount, bool pct, double? card, string message)
    {
        var input = Bill((decimal)tax, (decimal)service, (decimal)discount, pct, card is null ? null : (decimal)card);
        Assert.Equal(message, FirstError(new BillingSettingsInputValidator(), input));
        var ex = await Assert.ThrowsAsync<DomainException>(() => Admin.SetBillingSettings(input));
        Assert.Equal(message, ex.Message);
        Assert.Equal(16, (await _db.Restaurants.FirstAsync(r => r.Id == 1)).TaxPercent);   // nothing was saved
    }

    [Fact]
    public async Task Right_bill_settings_are_kept_exactly()
    {
        Assert.True(new BillingSettingsInputValidator().Validate(Bill(100, 0, 500, false, 5)).IsValid);
        await Admin.SetBillingSettings(Bill(17.5m, 10, 250, false, 5));
        var r = await _db.Restaurants.FirstAsync(x => x.Id == 1);
        Assert.Equal((17.5m, 10m, 250m, (decimal?)5m), (r.TaxPercent, r.ServiceChargePercent, r.DiscountValue, r.TaxPercentCard));
    }

    // ---- menu items ----
    private ItemInput Item(decimal price = 500, decimal? taxRate = null, decimal furtherTax = 0, decimal? deal = null,
        List<ModifierGroupInput>? groups = null) =>
        new(_category, "Karahi", "", "", price, "", true, 15, null, groups, taxRate, furtherTax, DealPrice: deal);

    [Fact]
    public async Task Item_tax_blank_means_the_restaurant_rate_and_zero_means_none()
    {
        var blank = await Menu.CreateItem(Item(taxRate: null));
        var zero = await Menu.CreateItem(Item(taxRate: 0));
        Assert.Null((await _db.MenuItems.FirstAsync(i => i.Id == blank)).TaxRate);
        Assert.Equal(0m, (await _db.MenuItems.FirstAsync(i => i.Id == zero)).TaxRate);
    }

    [Theory]
    [InlineData(101, 0, "Item tax % must be between 0 and 100.")]
    [InlineData(-1, 0, "Item tax % must be between 0 and 100.")]
    [InlineData(5, 101, "Further tax % must be between 0 and 100.")]
    public async Task Item_tax_outside_0_to_100_is_refused(double tax, double further, string message)
    {
        var input = Item(taxRate: (decimal)tax, furtherTax: (decimal)further);
        Assert.Equal(message, FirstError(new ItemInputValidator(), input));
        var ex = await Assert.ThrowsAsync<DomainException>(() => Menu.CreateItem(input));
        Assert.Equal(message, ex.Message);
        Assert.Empty(await _db.MenuItems.ToListAsync());
    }

    [Theory]
    [InlineData(500)]
    [InlineData(600)]
    [InlineData(-1)]
    public async Task A_deal_price_that_is_not_below_the_price_is_refused_not_dropped(double deal)
    {
        var input = Item(price: 500, deal: (decimal)deal);
        Assert.False(new ItemInputValidator().Validate(input).IsValid);
        await Assert.ThrowsAsync<DomainException>(() => Menu.CreateItem(input));
        Assert.Empty(await _db.MenuItems.ToListAsync());

        var id = await Menu.CreateItem(Item(price: 500, deal: 450));
        Assert.Equal(450m, (await _db.MenuItems.FirstAsync(i => i.Id == id)).DealPrice);
    }

    [Fact]
    public async Task A_refused_edit_leaves_the_dish_as_it_was()
    {
        var id = await Menu.CreateItem(Item(price: 500, deal: 400));
        await Assert.ThrowsAsync<DomainException>(() => Menu.UpdateItem(id, Item(price: 300, deal: 400)));
        _db.ChangeTracker.Clear();
        var item = await _db.MenuItems.FirstAsync(i => i.Id == id);
        Assert.Equal((500m, (decimal?)400m), (item.Price, item.DealPrice));
    }

    [Fact]
    public void Every_add_on_needs_a_name_and_a_price_of_0_or_more()
    {
        var noName = Item(groups: [new ModifierGroupInput("Extras", 0, 2, 1, [new ModifierOptionInput("", 50, 1)])]);
        var negative = Item(groups: [new ModifierGroupInput("Extras", 0, 2, 1, [new ModifierOptionInput("Cheese", -5, 1)])]);
        Assert.Equal("Give each option a name.", FirstError(new ItemInputValidator(), noName));
        Assert.Equal("Option price can't be negative.", FirstError(new ItemInputValidator(), negative));
    }

    // ---- orders and tickets ----
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void Each_dish_is_ordered_1_to_50_at_a_time(int qty, bool ok)
    {
        var line = new List<OrderLineRequest> { new(1, qty, null) };
        Assert.Equal(ok, new PlaceOrderRequestValidator().Validate(new PlaceOrderRequest("T1", line, null)).IsValid);
        Assert.Equal(ok, new TicketRequestValidator().Validate(new TicketRequest("Takeaway", null, null, null, null, null, line, null)).IsValid);
        Assert.Equal(ok, new GuestTicketRequestValidator().Validate(new GuestTicketRequest("Takeaway", "Ali", "03001234567", null, line, null)).IsValid);
    }

    private async Task<string> DeliveryTicket()
    {
        var item = await Menu.CreateItem(Item());
        var placed = await Tickets.Place(new TicketRequest("Delivery", "Ali", "03001234567", "House 5, Gulberg", 100, null,
            [new OrderLineRequest(item, 1, null)], null));
        return placed.Ticket.TableCode;
    }

    [Fact]
    public async Task A_delivery_keeps_its_phone_and_address()
    {
        var code = await DeliveryTicket();
        Assert.Contains("phone", (await Assert.ThrowsAsync<DomainException>(() =>
            Tickets.Update(code, new TicketUpdate(null, "  ", null, null, null)))).Message);
        Assert.Contains("address", (await Assert.ThrowsAsync<DomainException>(() =>
            Tickets.Update(code, new TicketUpdate(null, null, "", null, null)))).Message);
        Assert.Equal(Phone.Message, (await Assert.ThrowsAsync<DomainException>(() =>
            Tickets.Update(code, new TicketUpdate(null, "call me", null, null, null)))).Message);

        // Null leaves a field alone, as the rider screen sends it.
        var row = await Tickets.Update(code, new TicketUpdate(null, null, null, null, "Test Rider"));
        Assert.Equal(("03001234567", "House 5, Gulberg", "Test Rider"), (row.CustomerPhone, row.Address, row.RiderName));
    }

    [Fact]
    public async Task A_delivery_phone_must_be_a_real_number()
    {
        var item = await Menu.CreateItem(Item());
        var ex = await Assert.ThrowsAsync<DomainException>(() => Tickets.Place(new TicketRequest("Delivery", "Ali", "12", "House 5", 100, null,
            [new OrderLineRequest(item, 1, null)], null)));
        Assert.Equal(Phone.Message, ex.Message);
        Assert.Empty(await _db.Tables.Where(t => t.Kind != Domain.Ordering.OrderChannel.Dine).ToListAsync());
    }

    // ---- the rest of the forms ----
    [Fact]
    public void Fbr_branch_details_must_look_right()
    {
        BranchInput Branch(string? ip = null, string? url = null, long? pos = null) => new("Main", "", true, "Cloud", true, pos, null, url, ip);
        Assert.True(new BranchInputValidator().Validate(Branch("203.0.113.10", "https://gw.fbr.gov.pk/x", 123456)).IsValid);
        Assert.Equal("Enter the static IP like 203.0.113.10.", FirstError(new BranchInputValidator(), Branch(ip: "my office")));
        Assert.Equal("FBR address must start with http:// or https://.", FirstError(new BranchInputValidator(), Branch(url: "gw.fbr.gov.pk")));
        Assert.Equal("POS ID must be a number above 0.", FirstError(new BranchInputValidator(), Branch(pos: 0)));
    }

    [Fact]
    public void Fbr_numbers_fit_their_boxes()
    {
        Assert.True(new FbrSettingsInputValidator().Validate(new FbrSettingsInput(true, "Test Pvt Ltd", "1234567-8", "")).IsValid);
        Assert.Equal("NTN can only have numbers and dashes.", FirstError(new FbrSettingsInputValidator(), new FbrSettingsInput(true, "Test", "ABC123", "")));
        Assert.Equal("STRN must be 20 characters or fewer.", FirstError(new FbrSettingsInputValidator(), new FbrSettingsInput(true, "Test", "", new string('1', 21))));
    }

    [Fact]
    public void A_subscription_payment_needs_an_amount_and_whole_months()
    {
        Assert.True(new RecordPaymentRequestValidator().Validate(new RecordPaymentRequest("Monthly", 3, 9000, null)).IsValid);
        Assert.True(new RecordPaymentRequestValidator().Validate(new RecordPaymentRequest("Setup", 0, 15000, null)).IsValid);
        Assert.Equal("Enter the amount received.", FirstError(new RecordPaymentRequestValidator(), new RecordPaymentRequest("Monthly", 1, 0, null)));
        Assert.Equal("Months must be a whole number from 1 to 36.", FirstError(new RecordPaymentRequestValidator(), new RecordPaymentRequest("Monthly", 0, 3000, null)));
        Assert.Equal("Choose setup fee or monthly.", FirstError(new RecordPaymentRequestValidator(), new RecordPaymentRequest("Subscription", 1, 3000, null)));
    }

    [Fact]
    public void Supplier_bill_lines_fit_the_statement()
    {
        var longName = new PurchaseLineInput(null, new string('x', 121), 1, 10);
        Assert.Equal("Item name must be 120 characters or fewer.",
            FirstError(new PurchaseInputValidator(), new PurchaseInput(null, null, null, null, [longName], null, null)));
        Assert.Equal("Quantity must be more than zero.",
            FirstError(new SupplierReturnInputValidator(), new SupplierReturnInput(null, null, null, null, [new PurchaseLineInput(null, "Oil", 0, 10)])));
    }

    [Fact]
    public void A_new_password_has_at_least_6_characters()
    {
        Assert.Equal("New password must be at least 6 characters.", FirstError(new ChangePasswordRequestValidator(), new ChangePasswordRequest("old-pass", "123")));
        Assert.Equal("Enter your current password.", FirstError(new ChangePasswordRequestValidator(), new ChangePasswordRequest("", "new-pass")));
    }

    [Fact]
    public async Task The_guest_cart_knows_when_tax_comes_on_the_bill()
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == 1);
        r.TaxEnabled = true;
        await _db.SaveChangesAsync();
        Assert.True((await new MenuService(_db).GetMenu("test"))!.Restaurant.Pickup!.TaxOnBill);
        r.TaxEnabled = false;
        await _db.SaveChangesAsync();
        Assert.False((await new MenuService(_db).GetMenu("test"))!.Restaurant.Pickup!.TaxOnBill);
    }

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => "h:" + password;
        public bool Verify(string password, string hash) => hash == "h:" + password;
    }

    private sealed class QuietNotifier : IOrderNotifier
    {
        public Task OrderChanged(string slug, OrderDto order, CancellationToken ct = default) => Task.CompletedTask;
        public Task OrderMoved(string slug, string fromTableCode, OrderDto order, CancellationToken ct = default) => Task.CompletedTask;
        public Task ServiceRequestChanged(string slug, ServiceRequestDto request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoPrinter : IPrintDispatcher
    {
        public Task Dispatch(string slug, PrintJob job, CancellationToken ct = default) => Task.CompletedTask;
    }
}
