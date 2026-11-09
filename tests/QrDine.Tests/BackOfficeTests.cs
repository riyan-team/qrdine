using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Admin;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Application.Expenses;
using QrDine.Application.Inventory;
using QrDine.Application.Purchasing;
using QrDine.Application.Reports;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Inventory;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// Sign-in without a restaurant address, the supplier khata and the reports, against a real
/// (in-memory SQLite) DbContext so the tenant filter is exercised too.
/// </summary>
public class BackOfficeTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly MovableTenant _tenant = new();

    public BackOfficeTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private OnboardingService Onboarding => new(_db, new PlainHasher(), new FakeTokens());
    private AuthService Auth => new(_db, _tenant, new FakeTokens(), new PlainHasher(), new PermissionService(_db));

    private async Task<int> SignUp(string name, string mobile, string password = "secret1")
    {
        var res = await Onboarding.Onboard(new OnboardRequest(name, null, "Owner", mobile, password));
        var r = await _db.Restaurants.FirstAsync(x => x.Slug == res.Slug);
        return r.Id;
    }

    private async Task<int> OneRestaurant()
    {
        var id = await SignUp("Test Dhaba", "03001234567");
        _tenant.Set(id, "test-dhaba");
        return id;
    }

    // ---- names ----
    [Theory]
    [InlineData("0300 1234567", "03001234567")]
    [InlineData("+92 300 1234567", "03001234567")]
    [InlineData("92-300-1234567", "03001234567")]
    [InlineData("3001234567", "03001234567")]
    [InlineData("  Ali.Khan ", "ali.khan")]
    public void Sign_in_names_have_one_spelling(string typed, string stored) =>
        Assert.Equal(stored, LoginName.Normalize(typed));

    // ---- sign-up ----
    [Fact]
    public async Task Address_comes_from_the_name_and_never_clashes()
    {
        var a = await Onboarding.Onboard(new OnboardRequest("Karachi Biryani House!", null, "A", "03001111111", "secret1"));
        var b = await Onboarding.Onboard(new OnboardRequest("Karachi Biryani House", null, "B", "03002222222", "secret1"));
        Assert.Equal("karachi-biryani-house", a.Slug);
        Assert.Equal("karachi-biryani-house-2", b.Slug);
    }

    [Fact]
    public async Task A_name_in_urdu_still_gets_an_address()
    {
        var res = await Onboarding.Onboard(new OnboardRequest("کراچی بریانی", null, "A", "03001111111", "secret1"));
        Assert.Equal("restaurant", res.Slug);
    }

    [Fact]
    public async Task One_mobile_number_is_one_account()
    {
        await SignUp("First", "0300 1234567");
        var ex = await Assert.ThrowsAsync<DomainException>(() => SignUp("Second", "+923001234567"));
        Assert.Contains("already has an account", ex.Message);
        Assert.Equal(1, await _db.Restaurants.CountAsync());
    }

    // ---- sign-in ----
    [Fact]
    public async Task Sign_in_finds_the_right_restaurant_from_the_number_alone()
    {
        await SignUp("First Place", "03001111111", "first-pass");
        var second = await SignUp("Second Place", "03002222222", "second-pass");

        var login = await Auth.FindLogin(new LoginRequest("+92 300 2222222", "second-pass"));
        Assert.Equal(second, login.Restaurant.Id);

        var res = await Auth.CompleteLogin(login);
        Assert.Equal("second-place", res.Slug);
        Assert.Equal("Second Place", res.RestaurantName);
        Assert.Contains(Permissions.SuppliersManage, res.Permissions);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_number_read_the_same()
    {
        await SignUp("First", "03001111111", "right-pass");
        var wrong = await Assert.ThrowsAsync<DomainException>(() => Auth.FindLogin(new LoginRequest("03001111111", "nope")));
        var unknown = await Assert.ThrowsAsync<DomainException>(() => Auth.FindLogin(new LoginRequest("03009999999", "nope")));
        Assert.Equal(wrong.Message, unknown.Message);
    }

    [Fact]
    public async Task Staff_get_their_own_restaurants_permissions_at_sign_in()
    {
        var first = await SignUp("First", "03001111111");
        var second = await SignUp("Second", "03002222222");
        _tenant.Set(second, "second");
        await new RestaurantAdminService(_db, new PlainHasher(), _tenant)
            .AddStaff(new StaffInput("kashif", "Kashif", "Kitchen", "cook-pass"), callerIsOwner: true);

        // Take the kitchen's permission away in the other restaurant: it must make no difference here.
        var firstKitchen = await _db.RolePermissions.IgnoreQueryFilters()
            .Where(p => p.RestaurantId == first && p.Role == StaffRole.Kitchen).ToListAsync();
        _db.RolePermissions.RemoveRange(firstKitchen);
        await _db.SaveChangesAsync();

        _tenant.Clear();
        var res = await Auth.CompleteLogin(await Auth.FindLogin(new LoginRequest("kashif", "cook-pass")));
        Assert.Equal("Kitchen", res.Role);
        Assert.Equal(new[] { Permissions.OrdersCook }, res.Permissions);
    }

    [Fact]
    public async Task A_staff_username_can_only_be_used_once_anywhere()
    {
        await SignUp("First", "03001111111");
        var second = await SignUp("Second Place", "03002222222");
        _tenant.Set(1, "first");
        var admin = new RestaurantAdminService(_db, new PlainHasher(), _tenant);
        await admin.AddStaff(new StaffInput("ali", "Ali", "Waiter", "pass-one"), true);

        _tenant.Set(second, "second-place");
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            admin.AddStaff(new StaffInput("ALI", "Ali", "Waiter", "pass-two"), true));
        Assert.Contains("ali.second-place", ex.Message);
    }

    // ---- supplier khata ----
    private SupplierService Suppliers => new(_db);

    private async Task<(int supplier, int chicken)> Khata(decimal opening = 0, decimal stock = 10)
    {
        await OneRestaurant();
        var supplier = await Suppliers.Create(new SupplierInput("Bilal Poultry", "03005555555", null, opening));
        var item = new StockItem { Name = "Chicken", Unit = "kg", Quantity = stock, LowThreshold = 5 };
        _db.Stock.Add(item);
        await _db.SaveChangesAsync();
        return (supplier, item.Id);
    }

    [Fact]
    public async Task A_bill_raises_what_is_owed_and_moves_the_stock()
    {
        var (supplier, chicken) = await Khata(opening: 1000);
        await Suppliers.AddPurchase(supplier, new PurchaseInput("2026-11-01", "B-17", null, null,
            new() { new(chicken, null, 20, 600) }, null, null), "owner");

        var ledger = await Suppliers.Ledger(supplier);
        Assert.Equal(13000, ledger.Supplier.Balance);
        Assert.Equal(12000, ledger.Rows.Single().Bought);

        var stock = await _db.Stock.SingleAsync();
        Assert.Equal(30, stock.Quantity);
        Assert.Equal(600, stock.LastCost);
        var move = await _db.StockMovements.SingleAsync();
        Assert.Equal(StockMovementType.Restock, move.Type);
        Assert.Equal(20, move.Delta);
    }

    [Fact]
    public async Task Paying_part_of_a_bill_at_the_counter_is_two_lines_in_the_khata()
    {
        var (supplier, _) = await Khata();
        await Suppliers.AddPurchase(supplier, new PurchaseInput("2026-11-01", null, null, 5000, null, 2000, "Cash"), "owner");

        var ledger = await Suppliers.Ledger(supplier);
        Assert.Equal(3000, ledger.Supplier.Balance);
        Assert.Equal(new decimal[] { 5000, 3000 }, ledger.Rows.Select(r => r.Balance));
    }

    [Fact]
    public async Task Paying_more_than_the_bill_at_the_counter_is_refused_and_saves_nothing()
    {
        var (supplier, chicken) = await Khata();
        await Assert.ThrowsAsync<DomainException>(() => Suppliers.AddPurchase(supplier,
            new PurchaseInput(null, null, null, null, new() { new(chicken, null, 1, 100) }, 500, "Cash"), "owner"));
        Assert.Empty(await _db.SupplierEntries.ToListAsync());
        Assert.Equal(10, (await _db.Stock.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Deleting_a_bill_puts_stock_and_the_khata_back()
    {
        var (supplier, chicken) = await Khata();
        var bill = await Suppliers.AddPurchase(supplier, new PurchaseInput(null, null, null, null,
            new() { new(chicken, null, 5, 100) }, 200, "Cash"), "owner");

        await Suppliers.DeleteEntry(bill);

        Assert.Equal(10, (await _db.Stock.AsNoTracking().SingleAsync()).Quantity);
        Assert.Empty(await _db.SupplierEntries.ToListAsync());
        Assert.Equal(0, (await Suppliers.Ledger(supplier)).Supplier.Balance);
    }

    [Fact]
    public async Task A_return_lowers_what_is_owed_and_the_stock()
    {
        var (supplier, chicken) = await Khata();
        await Suppliers.AddPurchase(supplier, new PurchaseInput(null, null, null, null, new() { new(chicken, null, 10, 500) }, null, null), "o");
        await Suppliers.AddReturn(supplier, new SupplierReturnInput(null, null, "Bad batch", null, new() { new(chicken, null, 2, 500) }), "o");

        Assert.Equal(4000, (await Suppliers.Ledger(supplier)).Supplier.Balance);
        Assert.Equal(18, (await _db.Stock.AsNoTracking().SingleAsync()).Quantity);
    }

    [Fact]
    public async Task One_restaurant_cannot_see_anothers_khata()
    {
        var (supplier, _) = await Khata();
        var other = await SignUp("Other Place", "03002222222");
        _tenant.Set(other, "other-place");

        Assert.Empty(await Suppliers.List());
        await Assert.ThrowsAsync<DomainException>(() => Suppliers.Ledger(supplier));
    }

    [Fact]
    public async Task Waste_lowers_stock_and_is_recorded_as_waste()
    {
        var (_, chicken) = await Khata();
        await new InventoryService(_db).Waste(chicken, 2.5m, "Left out overnight");

        Assert.Equal(7.5m, (await _db.Stock.AsNoTracking().SingleAsync()).Quantity);
        Assert.Equal(StockMovementType.Waste, (await _db.StockMovements.SingleAsync()).Type);
    }

    // ---- reports ----
    private void Invoice(decimal total, string method, decimal tax = 0, decimal discount = 0, int session = 1, DateTime? at = null)
    {
        var branch = _db.Branches.First().Id;
        _db.Invoices.Add(new Invoice
        {
            BranchId = branch, TableSessionId = session, Number = 1 + _db.Invoices.Local.Count, Usin = Guid.NewGuid().ToString("N"),
            TableName = "Table 1", TableCode = "T1",
            Subtotal = total - tax + discount, TaxAmount = tax, DiscountAmount = discount, Total = total, PaymentMethod = method,
            IssuedAt = at ?? DateTime.UtcNow, CreatedByUsername = "cashier",
            Lines = { new InvoiceLine { Name = "Biryani", ItemCode = "0", Qty = 2, UnitPrice = (total - tax + discount) / 2 } },
        });
    }

    [Fact]
    public async Task Profit_is_sales_before_tax_less_purchases_and_expenses()
    {
        var (supplier, _) = await Khata();
        Invoice(11500, "Cash", tax: 1500);
        await _db.SaveChangesAsync();
        await Suppliers.AddPurchase(supplier, new PurchaseInput(null, null, null, 3000, null, null, null), "o");
        await new ExpenseService(_db).Create(new ExpenseInput(null, "Electricity", 2000, "Card", null, null, null), "o");

        var s = (await new BusinessReportService(_db, _tenant).Build(null, null, null)).Summary;
        Assert.Equal(11500, s.Sales);
        Assert.Equal(10000, s.SalesBeforeTax);
        Assert.Equal(5000, s.EstimatedProfit);
    }

    [Fact]
    public async Task Cash_in_hand_counts_only_cash_and_splits_bills_by_what_was_taken()
    {
        var (supplier, _) = await Khata();
        Invoice(1000, "Cash");
        Invoice(500, "Card", session: 2);
        Invoice(900, "Split (Cash+Card)", session: 7);
        await _db.SaveChangesAsync();
        _db.Payments.Add(new Payment { BranchId = _db.Branches.First().Id, TableSessionId = 7, Amount = 400, Method = "Cash" });
        _db.Payments.Add(new Payment { BranchId = _db.Branches.First().Id, TableSessionId = 7, Amount = 500, Method = "Card" });
        await _db.SaveChangesAsync();

        await new ExpenseService(_db).Create(new ExpenseInput(null, "Gas", 100, "Cash", null, null, null), "o");
        await new ExpenseService(_db).Create(new ExpenseInput(null, "Rent", 999, "Bank", null, null, null), "o");
        await Suppliers.AddPayment(supplier, new SupplierPaymentInput(null, 200, "Cash", null, null), "o");
        await Suppliers.AddPayment(supplier, new SupplierPaymentInput(null, 300, "JazzCash", null, null), "o");

        var c = (await new BusinessReportService(_db, _tenant).Build(null, null, null)).Closing;
        Assert.Equal(1400, c.CashSales);
        Assert.Equal(1000, c.OtherSales);
        Assert.Equal(100, c.CashExpenses);
        Assert.Equal(200, c.CashSupplierPayments);
        Assert.Equal(1100, c.CashInHand);
    }

    [Fact]
    public async Task A_bill_settled_late_at_night_counts_on_the_pakistan_day()
    {
        await OneRestaurant();
        // 20:30 UTC on the 1st is 01:30 on the 2nd in Pakistan.
        Invoice(700, "Cash", at: new DateTime(2026, 11, 1, 20, 30, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();
        var reports = new BusinessReportService(_db, _tenant);

        Assert.Equal(0, (await reports.Build("2026-11-01", "2026-11-01", null)).Summary.Sales);
        var second = await reports.Build("2026-11-02", "2026-11-02", null);
        Assert.Equal(700, second.Summary.Sales);
        Assert.Equal("1 am", second.Hours.Single().Label);
    }

    private sealed class MovableTenant : ICurrentTenant
    {
        public int? RestaurantId { get; private set; }
        public string? Slug { get; private set; }
        public bool IsResolved => RestaurantId != null;
        public void Set(int restaurantId, string slug) { RestaurantId = restaurantId; Slug = slug; }
        public void Clear() { RestaurantId = null; Slug = null; }
    }

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => "h:" + password;
        public bool Verify(string password, string hash) => hash == "h:" + password;
    }

    private sealed class FakeTokens : ITokenService
    {
        public string CreateStaffToken(StaffMember user, string slug, IEnumerable<string> permissions) => "staff";
        public string CreateGuestToken(int restaurantId, string slug, string tableCode) => "guest";
        public string CreatePlatformToken(PlatformAdmin admin) => "platform";
    }
}
