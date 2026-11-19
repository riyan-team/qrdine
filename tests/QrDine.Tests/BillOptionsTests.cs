using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QrDine.Application.Admin;
using QrDine.Application.Auth;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Application.Inventory;
using QrDine.Application.Loyalty;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Printing;
using QrDine.Application.Reports;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Inventory;
using QrDine.Domain.Menu;
using QrDine.Domain.Ordering;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Tables;
using QrDine.Infrastructure.Fiscal;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// The owner's on/off settings for bills and cancelling. Every one is off by default, and off is exactly how the
/// app always worked; each test turns one on and holds what it does: on the bill, the receipt, the reports and FBR.
/// </summary>
public class BillOptionsTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();
    private int _biryani, _naan, _tea, _water;

    public BillOptionsTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant);
        _db.Database.EnsureCreated();
        Seed().GetAwaiter().GetResult();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private OrderService Orders => new(_db, _tenant, new Notifier(), new NoPrinter(), new InventoryService(_db));
    private BillingService Billing => new(_db, _tenant, new Notifier(), new FiscalSyncService(_db, _tenant, new NoFiscal()),
        new NoPrinter(), new LoyaltyService(_db, _tenant), NullLogger<BillingService>.Instance);
    private BusinessReportService Reports => new(_db, _tenant);
    private RestaurantAdminService Admin => new(_db, new PlainHasher(), _tenant);

    private async Task Seed()
    {
        _tenant.Set(1, "test");
        _db.Restaurants.Add(new Restaurant { Id = 1, Name = "Test", Slug = "test", Currency = "PKR" });
        await _db.SaveChangesAsync();
        var branch = new Branch { Name = "Main" };
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();
        _db.Tables.Add(new DiningTable { BranchId = branch.Id, Code = "T1", Name = "Table 1" });
        var cat = new MenuCategory { Name = "Food" };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        var b = new MenuItem { CategoryId = cat.Id, Name = "Biryani", Price = 500 };          // the restaurant's rate
        var n = new MenuItem { CategoryId = cat.Id, Name = "Naan", Price = 50 };
        var t = new MenuItem { CategoryId = cat.Id, Name = "Tea", Price = 75, TaxRate = 16 };  // its own rate
        var w = new MenuItem { CategoryId = cat.Id, Name = "Water", Price = 40, TaxRate = 0 }; // tax-free
        _db.MenuItems.AddRange(b, n, t, w);
        await _db.SaveChangesAsync();
        _biryani = b.Id; _naan = n.Id; _tea = t.Id; _water = w.Id;
    }

    private async Task Configure(Action<Restaurant> change)
    {
        var r = await _db.Restaurants.FirstAsync();
        change(r);
        await _db.SaveChangesAsync();
    }

    private Task<OrderDto> Dine(params (int Item, int Qty)[] lines) =>
        Orders.Place(new PlaceOrderRequest("T1", lines.Select(l => new OrderLineRequest(l.Item, l.Qty, null)).ToList(), null), OrderSource.Waiter);

    private static List<InvoiceLine> Lines(params (decimal Unit, int Qty, decimal Tax)[] l) =>
        l.Select((x, i) => new InvoiceLine { ItemCode = i.ToString(), Name = "Item " + i, UnitPrice = x.Unit, Qty = x.Qty, TaxRate = x.Tax }).ToList();

    // ---------- the bill maths ----------

    [Fact]
    public void All_off_prices_the_bill_exactly_as_before()
    {
        var m = Invoice.Price(Lines((1000, 1, 16)), new Invoice.Charges(10), 100);
        Assert.Equal(144, m.TaxAmount);              // tax after the discount: 16% of 900
        Assert.Equal(100, m.ServiceChargeAmount);    // service on the full 1000, untaxed
        Assert.Equal(1144, m.Total);
        Assert.Equal(0, m.ServiceTaxAmount);
        Assert.Equal(0, m.RoundingAmount);
        Assert.Equal(m, Invoice.Price(Lines((1000, 1, 16)), 10, 100));   // the old call gives the same bill
    }

    [Fact]
    public void Service_after_discount_is_worked_out_on_the_food_after_the_discount()
    {
        var m = Invoice.Price(Lines((1000, 1, 16)), new Invoice.Charges(10, ServiceAfterDiscount: true), 100);
        Assert.Equal(90, m.ServiceChargeAmount);
        Assert.Equal(1000 + 90 + 144 - 100, m.Total);
    }

    [Fact]
    public void Tax_on_service_is_charged_on_the_service_charge_and_kept_apart()
    {
        var m = Invoice.Price(Lines((1000, 1, 16)), new Invoice.Charges(10, ServiceTaxRate: 16), 0);
        Assert.Equal(160, m.TaxAmount);
        Assert.Equal(100, m.ServiceChargeAmount);
        Assert.Equal(16, m.ServiceTaxAmount);
        Assert.Equal(1276, m.Total);
    }

    [Theory]
    [InlineData(1234.56, 1235, 0.44)]
    [InlineData(1234.40, 1234, -0.40)]
    [InlineData(1234.50, 1235, 0.50)]
    [InlineData(1234.00, 1234, 0)]
    public void Rounding_goes_to_the_nearest_rupee_and_is_kept_as_its_own_amount(decimal exact, decimal total, decimal rounding)
    {
        var m = Invoice.Price(Lines((exact, 1, 0)), new Invoice.Charges(0, RoundToRupee: true), 0);
        Assert.Equal(total, m.Total);
        Assert.Equal(rounding, m.RoundingAmount);
        Assert.Equal(exact, m.Total - m.RoundingAmount);
    }

    // ---------- through the till ----------

    [Fact]
    public async Task Taxed_service_reaches_the_invoice_the_reports_and_fbr()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; r.ServiceChargePercent = 10; r.ServiceChargeTaxable = true; });
        await Dine((_biryani, 2));                                                  // 1000

        var bill = await Billing.GetBill("T1");
        Assert.Equal(16, bill.ServiceTaxAmount);
        Assert.Equal(1276, bill.Total);

        var dto = await Billing.Settle("T1", new SettleRequest("Cash", 0, 1300));
        Assert.Equal(1276, dto.Total);
        Assert.Equal(16, dto.ServiceTaxAmount);
        Assert.Equal(24, dto.ChangeGiven);
        var inv = await _db.Invoices.Include(i => i.Lines).SingleAsync();
        Assert.Equal(16, inv.ServiceTaxPercent);

        Assert.Equal(176, (await Reports.Build(null, null, null)).Summary.Tax);       // item tax + service tax

        var fbr = FbrInvoiceMapper.Map(inv, new FiscalContext("Test", "1234567", "12-34", FbrMode.Cloud, true, null, 110014, "tok"));
        var service = fbr.Items.Single(i => i.ItemCode == "SERVICE");
        Assert.Equal(16, service.TaxRate);
        Assert.Equal(16, service.TaxCharged);
        Assert.Equal(116, service.TotalAmount);
        Assert.Equal(1276, fbr.TotalBillAmount);
        Assert.Equal(176, fbr.TotalTaxCharged);

        var slip = string.Join("\n", PrintJobs.Receipt(dto).Lines);
        Assert.Contains("Tax on service", slip);
    }

    [Fact]
    public async Task A_card_payment_taxes_the_service_at_the_card_rate()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; r.TaxPercentCard = 5; r.ServiceChargePercent = 10; r.ServiceChargeTaxable = true; });
        await Dine((_biryani, 2));
        var bill = await Billing.GetBill("T1", method: "Card");
        Assert.Equal(50, bill.TaxAmount);
        Assert.Equal(5, bill.ServiceTaxAmount);
        Assert.Equal(1155, bill.Total);
        Assert.Equal(1276, bill.SplitRemaining);   // parts are booked at the standard rate, service tax included
    }

    [Fact]
    public async Task A_rounded_bill_gives_change_on_the_rounded_total()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 15.5m; r.RoundTotals = true; });
        await Dine((_naan, 3));                                                     // 150 + 23.25 = 173.25

        var bill = await Billing.GetBill("T1");
        Assert.Equal(173, bill.Total);
        Assert.Equal(-0.25m, bill.RoundingAmount);

        var inv = await Billing.Settle("T1", new SettleRequest("Cash", 0, 200));
        Assert.Equal(173, inv.Total);
        Assert.Equal(27, inv.ChangeGiven);
        Assert.Equal(-0.25m, inv.RoundingAmount);
        Assert.Equal(173, (await Reports.Build(null, null, null)).Closing.CashSales);

        var fbr = FbrInvoiceMapper.Map(await _db.Invoices.Include(i => i.Lines).SingleAsync(),
            new FiscalContext("Test", "1234567", "12-34", FbrMode.Cloud, true, null, 110014, "tok"));
        Assert.Equal(173.25, fbr.TotalBillAmount, 2);                               // FBR gets the exact amounts
    }

    [Fact]
    public async Task The_card_rate_replaces_an_items_own_rate_only_when_the_owner_says_so()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; r.TaxPercentCard = 5; });
        await Dine((_tea, 2), (_water, 1));                                         // tea 150 at its own 16%, water tax-free

        Assert.Equal(24, (await Billing.GetBill("T1", method: "Card")).TaxAmount);  // own rate kept
        await Configure(r => r.CardRateForAllItems = true);
        Assert.Equal(7.5m, (await Billing.GetBill("T1", method: "Card")).TaxAmount); // card rate; water still 0
        Assert.Equal(24, (await Billing.GetBill("T1", method: "Cash")).TaxAmount);   // cash is untouched
    }

    [Fact]
    public async Task The_discount_report_counts_the_service_that_came_off_with_the_discount()
    {
        await Configure(r => { r.ServiceChargePercent = 10; r.ServiceAfterDiscount = true; });
        await Dine((_biryani, 2));                                                  // 1000
        var inv = await Billing.Settle("T1", new SettleRequest("Cash", 100, 990));
        Assert.Equal(990, inv.Total);                                               // 900 food + 90 service
        var row = (await Reports.Build(null, null, null)).Discounts.Single();
        Assert.Equal(1100, row.Before);                                             // 1000 food + 100 service
    }

    // ---------- cancelling ----------

    private async Task<OrderDto> Cooking(params (int Item, int Qty)[] lines)
    {
        var o = await Dine(lines);
        await Orders.Approve(o.Id);
        return await Orders.Cook(o.Id, 10);
    }

    private async Task<OrderDto> Served(params (int Item, int Qty)[] lines)
    {
        var o = await Cooking(lines);
        await Orders.Ready(o.Id);
        return await Orders.Serve(o.Id);
    }

    private static readonly Canceller Owner = new(IsOwner: true, AtCounter: true);
    private static readonly Canceller Cashier = new(AtCounter: true);
    private static readonly Canceller Waiter = new();

    [Fact]
    public async Task By_default_food_that_is_cooking_can_still_be_cancelled()
    {
        var o = await Cooking((_biryani, 1));
        Assert.Equal("Cancelled", (await Orders.Cancel(o.Id, "Guest left", who: Waiter)).Status);
    }

    [Fact]
    public async Task Before_the_kitchen_starts_cooking_cancelling_works_as_normal_even_when_locked()
    {
        await Configure(r => r.NoCancelOnceCooking = true);
        var placed = await Dine((_biryani, 1));
        Assert.True(await Orders.GuestMayCancel(placed));
        Assert.Equal("Cancelled", (await Orders.Cancel(placed.Id, "Guest left", who: Canceller.Guest)).Status);

        var approved = await Dine((_naan, 1));
        await Orders.Approve(approved.Id);
        Assert.Equal("Cancelled", (await Orders.Cancel(approved.Id, "Wrong table", who: Waiter)).Status);
    }

    [Fact]
    public async Task Once_cooking_starts_a_locked_order_is_left_to_the_owner()
    {
        await Configure(r => r.NoCancelOnceCooking = true);
        var o = await Cooking((_biryani, 1));

        var guest = await Assert.ThrowsAsync<DomainException>(() => Orders.Cancel(o.Id, "Too slow", who: Canceller.Guest));
        Assert.Contains("already being made", guest.Message);
        Assert.False(await Orders.GuestMayCancel(o));
        var staff = await Assert.ThrowsAsync<DomainException>(() => Orders.Cancel(o.Id, "Guest left", who: Cashier));
        Assert.Contains("only the owner", staff.Message);

        Assert.Equal("Cancelled", (await Orders.Cancel(o.Id, "Guest left", who: Owner)).Status);
    }

    [Fact]
    public async Task The_cashier_option_cancels_at_any_stage_but_only_at_the_counter()
    {
        await Configure(r => { r.NoCancelOnceCooking = true; r.CashierCancelAnyStage = true; });
        var cooking = await Cooking((_biryani, 1));
        await Assert.ThrowsAsync<DomainException>(() => Orders.Cancel(cooking.Id, "Guest left", who: Waiter));
        Assert.Equal("Cancelled", (await Orders.Cancel(cooking.Id, "Guest left", who: Cashier)).Status);

        var served = await Served((_naan, 2));
        Assert.Equal("Cancelled", (await Orders.Cancel(served.Id, "Guest refused the dish", who: Cashier)).Status);

        await Configure(r => r.CashierCancelAnyStage = false);
        var again = await Served((_naan, 1));
        await Assert.ThrowsAsync<DomainException>(() => Orders.Cancel(again.Id, "Guest refused", who: Owner));  // served: never, without the option
    }

    [Fact]
    public async Task Taking_a_dish_off_follows_the_same_rules()
    {
        await Configure(r => r.NoCancelOnceCooking = true);
        var o = await Served((_biryani, 1), (_naan, 2));
        var naan = o.Items.Single(i => i.Name == "Naan").Id;

        await Assert.ThrowsAsync<DomainException>(() =>
            Orders.VoidItem(o.Id, naan, new VoidItemRequest(1, "Cold"), "cashier", who: Cashier));
        await Orders.VoidItem(o.Id, naan, new VoidItemRequest(1, "Cold"), "owner", who: Owner);

        await Configure(r => r.CashierCancelAnyStage = true);
        var after = await Orders.VoidItem(o.Id, naan, new VoidItemRequest(1, "Cold"), "cashier", who: Cashier);
        Assert.DoesNotContain(after.Items, i => i.Name == "Naan");
    }

    [Fact]
    public async Task Keep_stock_leaves_cooked_food_used_up_but_untouched_food_always_goes_back()
    {
        await Configure(r => { r.StockTrackingEnabled = true; r.KeepStockOnCookedCancel = true; });
        var rice = new StockItem { Name = "Rice", Unit = "kg", Quantity = 10 };
        _db.Stock.Add(rice);
        await _db.SaveChangesAsync();
        var item = await _db.MenuItems.Include(m => m.Recipe).FirstAsync(m => m.Id == _biryani);
        item.Recipe.Add(new RecipeLine { StockItemId = rice.Id, QtyPerServing = 0.5m });
        await _db.SaveChangesAsync();
        decimal Rice() => _db.Stock.AsNoTracking().First().Quantity;

        var cooking = await Cooking((_biryani, 2));
        Assert.Equal(9, Rice());
        await Orders.Cancel(cooking.Id, "Guest left");
        Assert.Equal(9, Rice());                                                    // made, so it stays used

        var approved = await Dine((_biryani, 2));
        await Orders.Approve(approved.Id);
        Assert.Equal(8, Rice());
        await Orders.Cancel(approved.Id, "Wrong table");
        Assert.Equal(9, Rice());                                                    // never cooked: back it goes

        await Configure(r => r.KeepStockOnCookedCancel = false);
        var again = await Cooking((_biryani, 2));
        await Orders.Cancel(again.Id, "Guest left");
        Assert.Equal(9, Rice());                                                    // the default: back as always
    }

    // ---------- the settings themselves ----------

    private static BillingSettingsInput Input(bool? allowCancel = null, bool? anyStage = null, bool? round = null) =>
        new(false, "GST", 16, 0, false, false, 0, true, true, null,
            RoundTotals: round, AllowCancelAfterCooking: allowCancel, CashierCancelAnyStage: anyStage);

    [Fact]
    public async Task Only_the_owner_changes_the_cancelling_rules_and_a_blank_field_leaves_a_setting_alone()
    {
        var off = await Admin.GetBillingSettings();
        Assert.True(off.AllowCancelAfterCooking);
        Assert.True(off.RestockCookedCancel);
        Assert.False(off.CashierCancelAnyStage || off.RoundTotals || off.ServiceChargeTaxable || off.ServiceAfterDiscount || off.CardRateForAllItems);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Admin.SetBillingSettings(Input(allowCancel: false), callerIsOwner: false));
        Assert.Contains("Only the owner", ex.Message);
        await Admin.SetBillingSettings(Input(allowCancel: true, round: true), callerIsOwner: false);   // unchanged rule, other option on: fine

        await Admin.SetBillingSettings(Input(allowCancel: false, anyStage: true));
        var now = await Admin.GetBillingSettings();
        Assert.False(now.AllowCancelAfterCooking);
        Assert.True(now.CashierCancelAnyStage);
        Assert.True(now.RoundTotals);

        await Admin.SetBillingSettings(Input());                                    // an older screen sends none of them
        Assert.Equal(now, await Admin.GetBillingSettings());
    }

    private sealed class Notifier : IOrderNotifier
    {
        public Task OrderChanged(string slug, OrderDto order, CancellationToken ct = default) => Task.CompletedTask;
        public Task OrderMoved(string slug, string fromTableCode, OrderDto order, CancellationToken ct = default) => Task.CompletedTask;
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

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => "h:" + password;
        public bool Verify(string password, string hash) => hash == "h:" + password;
    }
}
