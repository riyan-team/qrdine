using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Application.Customers;
using QrDine.Application.Expenses;
using QrDine.Application.Inventory;
using QrDine.Application.Loyalty;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Printing;
using QrDine.Application.Reports;
using QrDine.Application.Staffing;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Inventory;
using QrDine.Domain.Menu;
using QrDine.Domain.Ordering;
using QrDine.Domain.Tables;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// The money rules a full check of the bill, tax and counts turned up, each held in place: change against what
/// was left, no part bigger than the bill, the discount off the food only, a 0% item is tax-free, every screen
/// counting the money the same way, a bonus paid once, and no stock or money left behind by a cancel.
/// </summary>
public class MoneyLogicTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();
    private int _biryani, _naan;

    public MoneyLogicTests()
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
    private TicketService Tickets => new(_db, Orders, _tenant);
    private BusinessReportService Reports => new(_db, _tenant);

    private async Task Seed()
    {
        _tenant.Set(1, "test");
        _db.Restaurants.Add(new Domain.Restaurants.Restaurant { Id = 1, Name = "Test", Slug = "test", Currency = "PKR" });
        await _db.SaveChangesAsync();
        var branch = new Domain.Restaurants.Branch { Name = "Main" };
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();
        foreach (var code in new[] { "T1", "T2" })
            _db.Tables.Add(new DiningTable { BranchId = branch.Id, Code = code, Name = "Table " + code[1..] });
        var cat = new MenuCategory { Name = "Food" };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        var b = new MenuItem { CategoryId = cat.Id, Name = "Biryani", Price = 500 };
        var n = new MenuItem { CategoryId = cat.Id, Name = "Naan", Price = 50 };
        _db.MenuItems.AddRange(b, n);
        await _db.SaveChangesAsync();
        _biryani = b.Id; _naan = n.Id;
    }

    private async Task Configure(Action<Domain.Restaurants.Restaurant> change)
    {
        var r = await _db.Restaurants.FirstAsync();
        change(r);
        await _db.SaveChangesAsync();
    }

    private Task<OrderDto> Dine(string table, int biryani) =>
        Orders.Place(new PlaceOrderRequest(table, new List<OrderLineRequest> { new(_biryani, biryani, null) }, null), OrderSource.Waiter);

    private Task<TicketPlaced> Delivery(int biryani, decimal charge) =>
        Tickets.Place(new TicketRequest("Delivery", "Ali", "03001234567", "House 5, Gulberg", charge, null,
            new List<OrderLineRequest> { new(_biryani, biryani, null) }, null));

    // ---------- split bills and change ----------

    [Fact]
    public async Task Change_on_a_part_paid_bill_is_worked_out_against_what_was_left()
    {
        await Dine("T1", 2);                                                       // 1000
        await Billing.PayPartial("T1", new PayPartRequest(300, "Card"));
        Assert.Equal(700, (await Billing.GetBill("T1")).Remaining);

        var inv = await Billing.Settle("T1", new SettleRequest("Cash", 0, 1000));   // 1000 handed over for the 700
        Assert.Equal(300, inv.ChangeGiven);
        Assert.Equal(inv.Total + inv.ChangeGiven, inv.AmountTendered);              // received minus change is the bill
        var rep = await Reports.Build(null, null, null);
        Assert.Equal(700, rep.Closing.CashSales);                                   // the drawer kept 700 of it
    }

    [Fact]
    public async Task A_part_bigger_than_what_is_left_is_refused_and_the_drawer_counts_the_bill()
    {
        await Dine("T1", 2);                                                       // 1000
        await Billing.PayPartial("T1", new PayPartRequest(300, "Cash"));
        var ex = await Assert.ThrowsAsync<DomainException>(() => Billing.PayPartial("T1", new PayPartRequest(1000, "Cash")));
        Assert.Contains("700", ex.Message);

        var last = await Billing.PayPartial("T1", new PayPartRequest(700, "Cash"));
        Assert.True(last.Settled);
        Assert.Equal(0, last.Invoice!.ChangeGiven);
        var rep = await Reports.Build(null, null, null);
        var close = await new DayCloseService(_db, Reports).View(null, null);
        Assert.Equal(1000, rep.Closing.CashSales);
        Assert.Equal(1000, close.Expected);
    }

    [Fact]
    public void Older_overpaid_parts_count_only_what_the_bill_kept()
    {
        var bill = new Invoice { Id = 1, TableSessionId = 5, Total = 1000, PaymentMethod = "Cash" };
        var cashOnly = MoneyTaken.ByMethod(new[] { bill }, new[]
        {
            new Payment { Id = 1, TableSessionId = 5, Amount = 300, Method = "Cash" },
            new Payment { Id = 2, TableSessionId = 5, Amount = 1000, Method = "Cash" },
        });
        Assert.Equal(1000, cashOnly.Where(t => t.Method == "Cash").Sum(t => t.Amount));

        var cardThenCash = MoneyTaken.ByMethod(new[] { bill }, new[]
        {
            new Payment { Id = 1, TableSessionId = 5, Amount = 300, Method = "Card" },
            new Payment { Id = 2, TableSessionId = 5, Amount = 1000, Method = "Cash" },
        });
        Assert.Equal(300, cardThenCash.Single(t => t.Method == "Card").Amount);
        Assert.Equal(700, cardThenCash.Single(t => t.Method == "Cash").Amount);     // the 300 change came out of the cash
    }

    [Fact]
    public async Task Raising_the_discount_below_what_is_paid_is_refused()
    {
        await Dine("T1", 2);                                                       // 1000
        await Billing.PayPartial("T1", new PayPartRequest(600, "Cash"));
        await Assert.ThrowsAsync<DomainException>(() => Billing.Settle("T1", new SettleRequest("Cash", 500)));
        await Assert.ThrowsAsync<DomainException>(() => Billing.PayPartial("T1", new PayPartRequest(100, "Cash", 500)));
        Assert.True((await Billing.Settle("T1", new SettleRequest("Cash", 100))).Total == 900);
    }

    [Fact]
    public async Task Settling_against_an_old_total_is_refused()
    {
        await Dine("T1", 2);
        var seen = (await Billing.GetBill("T1")).Remaining;                         // 1000
        await Dine("T1", 1);                                                       // another order lands: 1500
        var ex = await Assert.ThrowsAsync<DomainException>(() => Billing.Settle("T1", new SettleRequest("Cash", 0, 0, null, seen)));
        Assert.Contains("1,500", ex.Message);
        Assert.Equal(1500, (await Billing.Settle("T1", new SettleRequest("Cash", 0, 0, null, 1500))).Total);
    }

    [Fact]
    public async Task A_split_is_shared_at_the_standard_rate_even_when_card_has_a_lower_one()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; r.TaxPercentCard = 5; });
        await Dine("T1", 2);                                                       // 1000
        var card = await Billing.GetBill("T1", null, default, "Card");
        Assert.Equal(1050, card.Total);
        Assert.Equal(1160, card.SplitRemaining);                                    // parts are booked at 16%

        var share = card.SplitRemaining / 2;
        var first = await Billing.PayPartial("T1", new PayPartRequest(share, "Card"));
        Assert.Equal(share, first.Remaining);                                       // the second guest pays the same
    }

    // ---------- discount and tax ----------

    [Fact]
    public async Task The_discount_comes_off_the_food_never_the_delivery_charge()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; });
        var t = await Delivery(2, 150);                                            // food 1000 + delivery 150
        var preview = await Billing.GetBill(t.Ticket.TableCode, 100);
        Assert.Equal(144, preview.TaxAmount);                                       // 900 × 16%
        Assert.Equal(16, preview.TaxPercent);

        var inv = await Billing.Settle(t.Ticket.TableCode, new SettleRequest("Cash", 100));
        Assert.Equal(144, inv.TaxAmount);
        Assert.Equal(16, inv.TaxPercent);                                           // printed rate is the real rate
        Assert.Equal(1194, inv.Total);                                              // 1150 − 100 + 144

        var line = await _db.Invoices.Include(i => i.Lines).SelectMany(i => i.Lines).FirstAsync(l => l.ItemCode == InvoiceLine.DeliveryCode);
        Assert.Equal(0, line.DiscountAmount);
    }

    [Fact]
    public async Task A_discount_bigger_than_the_food_stops_at_the_food()
    {
        var t = await Delivery(2, 150);
        var bill = await Billing.GetBill(t.Ticket.TableCode, 5000);
        Assert.Equal(1000, bill.DiscountAmount);
        Assert.Equal(150, bill.Total);                                              // the delivery is still paid for
    }

    [Fact]
    public async Task An_item_at_zero_tax_is_tax_free_and_a_blank_one_takes_the_restaurant_rate()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; });
        (await _db.MenuItems.FirstAsync(m => m.Id == _naan)).TaxRate = 0;
        await _db.SaveChangesAsync();
        await Orders.Place(new PlaceOrderRequest("T1", new List<OrderLineRequest> { new(_naan, 10, null), new(_biryani, 1, null) }, null), OrderSource.Waiter);

        var bill = await Billing.GetBill("T1");
        Assert.Equal(80, bill.TaxAmount);                                           // only the biryani: 500 × 16%
        Assert.Equal(16, bill.TaxPercent);                                          // the tax-free naan doesn't water the rate down
    }

    [Fact]
    public async Task Further_tax_is_in_the_total_and_shown_on_its_own()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; });
        (await _db.MenuItems.FirstAsync(m => m.Id == _biryani)).FurtherTaxRate = 3;
        await _db.SaveChangesAsync();
        await Dine("T1", 2);                                                       // 1000

        var bill = await Billing.GetBill("T1");
        Assert.Equal(30, bill.FurtherTaxAmount);
        Assert.Equal(1190, bill.Total);
        var inv = await Billing.Settle("T1", new SettleRequest("Cash", 0));
        Assert.Equal(30, inv.FurtherTaxAmount);
        Assert.Equal(inv.Subtotal - inv.DiscountAmount + inv.ServiceChargeAmount + inv.TaxAmount + inv.FurtherTaxAmount, inv.Total);
    }

    [Fact]
    public async Task Every_open_bill_total_includes_tax_service_and_delivery()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; r.ServiceChargePercent = 5; });
        await Dine("T1", 2);                                                       // 1000 + 160 tax + 50 service
        var t = await Delivery(2, 150);                                            // 1000 + 160 tax + 150, no service
        await Billing.PayPartial("T1", new PayPartRequest(210, "Cash"));

        var totals = await Billing.OpenBillTotals();
        Assert.Equal(new OpenBillTotal(1210, 1000), totals["T1"]);
        Assert.Equal(new OpenBillTotal(1310, 1310), totals[t.Ticket.TableCode]);
        Assert.Equal((await Billing.GetBill(t.Ticket.TableCode)).Total, totals[t.Ticket.TableCode].Total);
    }

    // ---------- every screen counts the money the same way ----------

    [Fact]
    public async Task Payment_methods_and_day_closing_agree_on_a_khata_bill_with_a_cash_part()
    {
        var customer = await new CustomerService(_db).Create(new CustomerInput("Office", null, null, null, 0, 0));
        await Dine("T1", 2);                                                       // 1000
        await Billing.PayPartial("T1", new PayPartRequest(400, "Cash"));
        await Billing.Settle("T1", new SettleRequest("Khata", 0, 0, customer));

        var rep = await Reports.Build(null, null, null);
        Assert.Equal(400, rep.Payments.Single(p => p.Name == "Cash").Amount);
        Assert.Equal(600, rep.Payments.Single(p => p.Name == "Khata").Amount);
        Assert.Equal(rep.Closing.Methods.Select(m => (m.Name, m.Amount)), rep.Payments.Select(m => (m.Name, m.Amount)));

        var dash = await new ReportsService(_db, _tenant).GetDashboard(7);
        Assert.Equal(400, dash.Payments.Single(p => p.Method == "Cash").Amount);
        Assert.Equal(600, dash.Payments.Single(p => p.Method == "Khata").Amount);
    }

    [Fact]
    public async Task Best_sellers_leave_out_the_delivery_charge()
    {
        var t = await Delivery(1, 900);
        await Billing.Settle(t.Ticket.TableCode, new SettleRequest("Cash", 0));
        var dash = await new ReportsService(_db, _tenant).GetDashboard(7);
        Assert.DoesNotContain(dash.TopItems, x => x.Name == "Delivery charge");
        Assert.Contains(dash.TopItems, x => x.Name == "Biryani");
    }

    [Fact]
    public async Task The_discount_report_shows_the_real_bill_before_the_discount()
    {
        await Configure(r => { r.TaxEnabled = true; r.TaxPercent = 16; });
        await Dine("T1", 2);                                                       // 1000 + 160 = 1160 with no discount
        await Billing.Settle("T1", new SettleRequest("Cash", 100));                 // 900 + 144 = 1044
        var row = (await Reports.Build(null, null, null)).Discounts.Single();
        Assert.Equal(1044, row.Total);
        Assert.Equal(1160, row.Before);
    }

    // ---------- payroll ----------

    [Fact]
    public async Task A_bonus_is_paid_once()
    {
        var payroll = new PayrollService(_db);
        var id = await payroll.Create(new EmployeeInput("Cook", null, null, 30000, null, null));
        await payroll.AddEntry(id, new PayrollEntryInput("Bonus", 1000, null, null, "Cash", null), "o");
        var month = await payroll.Month(null);
        Assert.Equal(30000, month.Rows.Single().Due);                               // the bonus already left the till
        Assert.Equal(1000, month.TotalPaid);

        await payroll.AddEntry(id, new PayrollEntryInput("Salary", month.Rows.Single().Due, null, null, "Cash", null), "o");
        Assert.Equal(0, (await payroll.Month(null)).Rows.Single().Due);
        Assert.Equal(31000, (await new ExpenseService(_db).List(null, null, null, null)).Total);
    }

    // ---------- cancelling ----------

    [Fact]
    public async Task The_last_order_on_a_part_paid_table_cant_be_cancelled()
    {
        var first = await Dine("T1", 2);
        var second = await Dine("T1", 1);
        await Billing.PayPartial("T1", new PayPartRequest(300, "Cash"));

        await Orders.Cancel(second.Id, "Wrong dish", by: "owner");                 // another order is still open: fine
        var ex = await Assert.ThrowsAsync<DomainException>(() => Orders.Cancel(first.Id, "Kitchen out of rice"));
        Assert.Contains("already paid", ex.Message);
        Assert.Equal(700, (await Billing.GetBill("T1")).Remaining);
    }

    [Fact]
    public async Task A_cancel_records_who_and_when()
    {
        var o = await Dine("T1", 1);
        await Orders.Cancel(o.Id, "Guest left", by: "owner");
        var saved = await _db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal("owner", saved.CancelledBy);
        Assert.NotNull(saved.CancelledAt);
        var row = (await Reports.Build(null, null, null)).Cancelled.Single();
        Assert.Equal("Guest left", row.Reason);
        Assert.False(string.IsNullOrEmpty(row.By));
    }

    [Fact]
    public async Task Cooking_straight_from_placed_takes_stock_so_a_cancel_gives_back_only_what_left()
    {
        await Configure(r => r.StockTrackingEnabled = true);
        var rice = new StockItem { Name = "Rice", Unit = "kg", Quantity = 10 };
        _db.Stock.Add(rice);
        await _db.SaveChangesAsync();
        var item = await _db.MenuItems.Include(m => m.Recipe).FirstAsync(m => m.Id == _biryani);
        item.Recipe.Add(new RecipeLine { StockItemId = rice.Id, QtyPerServing = 0.5m });
        await _db.SaveChangesAsync();

        var o = await Dine("T1", 2);
        await Orders.Cook(o.Id, 10);
        Assert.Equal(9, (await _db.Stock.AsNoTracking().FirstAsync()).Quantity);
        await Orders.Cancel(o.Id, "Guest left");
        Assert.Equal(10, (await _db.Stock.AsNoTracking().FirstAsync()).Quantity);
    }

    // ---------- days ----------

    [Fact]
    public async Task Order_history_counts_whole_pakistan_days()
    {
        var o = await Dine("T1", 1);
        // 20:30 UTC on the 19th is 01:30 on the 20th in Pakistan.
        var row = await _db.Orders.FirstAsync(x => x.Id == o.Id);
        _db.Entry(row).Property(x => x.CreatedAt).CurrentValue = new DateTime(2026, 11, 19, 20, 30, 0, DateTimeKind.Utc);
        await _db.SaveChangesAsync();

        var day20 = new DateTime(2026, 11, 20);
        Assert.Single((await Orders.OrdersHistory(null, day20, day20, null)).Rows);
        var day19 = new DateTime(2026, 11, 19);
        Assert.Empty((await Orders.OrdersHistory(null, day19, day19, null)).Rows);
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
}
