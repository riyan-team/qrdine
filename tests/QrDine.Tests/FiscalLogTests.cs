using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QrDine.Application.Billing;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure.Fiscal;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// The tax office log: every try at sending a bill is written down with how it started, who pressed, the JSON sent,
/// the answer and how it ended. These run the real HTTP transport against a stand-in tax office.
/// </summary>
public class FiscalLogTests : IDisposable
{
    private const string Token = "secret-token-1234";
    private const string Accepted = """{"InvoiceNumber":"110014LOG1","Code":"100","Response":"Fiscal Invoice Number generated successfully."}""";

    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _opts;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();

    public FiscalLogTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(_opts, _tenant);
        _db.Database.EnsureCreated();
        _db.Restaurants.Add(new Restaurant { Slug = "test", Name = "Test", FeatureFbr = true, FbrEnabled = true, Ntn = "1234567-8" });
        _db.SaveChanges();
        _db.Branches.Add(new Branch
        {
            RestaurantId = 1, Name = "Main", FbrMode = FbrMode.Cloud, FbrPosId = 110014, FbrToken = Token,
            FiscalAuthority = FiscalAuthority.FBR, FiscalEnvironment = FiscalEnvironment.Test,
        });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private int _n;
    private async Task<Invoice> Bill()
    {
        _n++;
        var line = new InvoiceLine { ItemCode = "1", Name = "Karahi", PctCode = "9801.0000", UnitPrice = 1000, Qty = 1, TaxRate = 16 };
        var inv = Invoice.Create(1, _n, "T1", "Table 1", new[] { line }, 0, 0, "Cash", _n, $"USIN{_n:D6}");
        inv.BranchId = 1;
        inv.FiscalStatus = FiscalStatus.Pending;
        inv.CreatedByUsername = "cashier";
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync();
        return inv;
    }

    private FiscalSyncService Svc(IFiscalService fiscal) => new(_db, _tenant, fiscal, new DefaultFiscalCapabilities());

    private static FbrFiscalService Office(HttpStatusCode status, string answer, bool throws = false) =>
        new(new HttpClient(new FiscalFilingTests.StandIn(status, answer, throws)), Options.Create(new FiscalOptions { Simulate = false }),
            NullLogger<FbrFiscalService>.Instance);

    [Fact]
    public async Task A_filed_bill_logs_the_json_sent_and_the_answer_but_never_the_token()
    {
        var inv = await Bill();
        await Svc(Office(HttpStatusCode.OK, Accepted)).Retry(inv.Id, "cashier");

        var page = await Svc(Office(HttpStatusCode.OK, Accepted)).Log(null, false, null, null);
        var row = Assert.Single(page.Rows);
        Assert.Equal((FiscalOutcome.Filed, FiscalTrigger.Manual, "cashier", 1), (row.Outcome, row.Trigger, row.By, row.Attempt));
        Assert.Equal(("FBR", "Test", 200, "110014LOG1"), (row.Office, row.Environment, row.HttpStatus, row.FiscalNumber));
        Assert.Equal(inv.Id, row.InvoiceId);
        Assert.Equal(110014, row.PosId);
        Assert.True(row.HasPayload);
        Assert.Equal(1, page.Last24h.Filed);

        var full = await Svc(Office(HttpStatusCode.OK, Accepted)).LogLine(row.Id);
        Assert.Equal("https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData", full.Endpoint);
        Assert.Contains("\"USIN\":\"USIN000001\"", full.RequestBody);
        Assert.Contains("110014LOG1", full.ResponseBody);
        Assert.DoesNotContain(Token, full.RequestBody);
        Assert.DoesNotContain(Token, full.ResponseBody);
    }

    [Fact]
    public async Task A_refusal_logs_the_http_status_and_reason_and_a_retry_adds_a_second_line()
    {
        var inv = await Bill();
        await Svc(Office(HttpStatusCode.BadRequest, """{"error":"Invalid PCT code"}""")).Retry(inv.Id, "cashier");
        await Svc(Office(HttpStatusCode.OK, Accepted)).RetryMany(new[] { inv.Id }, "owner");

        var rows = (await Svc(Office(HttpStatusCode.OK, Accepted)).Log(inv.Id, false, null, null)).Rows;
        Assert.Equal(2, rows.Count);
        // Newest first.
        Assert.Equal((FiscalOutcome.Filed, FiscalTrigger.Picked, "owner", 2), (rows[0].Outcome, rows[0].Trigger, rows[0].By, rows[0].Attempt));
        Assert.Equal((FiscalOutcome.Refused, 400, 1), (rows[1].Outcome, rows[1].HttpStatus, rows[1].Attempt));
        Assert.Contains("Invalid PCT code", rows[1].Error);

        var problems = (await Svc(Office(HttpStatusCode.OK, Accepted)).Log(null, true, null, null)).Rows;
        Assert.Equal(FiscalOutcome.Refused, Assert.Single(problems).Outcome);
    }

    [Fact]
    public async Task No_internet_logs_waiting_and_the_automatic_resend_is_logged_with_no_one_pressing()
    {
        var r = await _db.Restaurants.FirstAsync();
        var inv = await Bill();
        await Svc(Office(HttpStatusCode.OK, "", throws: true)).SyncOnSettle(inv, r, "cashier");
        await _db.SaveChangesAsync();
        await _db.Invoices.Where(i => i.Id == inv.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.FiscalLastAttemptAt, DateTime.UtcNow.AddHours(-1)));   // due again
        Assert.Equal(1, await Svc(Office(HttpStatusCode.OK, Accepted)).AutoRetry());

        var rows = (await Svc(Office(HttpStatusCode.OK, Accepted)).Log(null, false, null, null)).Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal((FiscalOutcome.Filed, FiscalTrigger.Auto, (string?)null), (rows[0].Outcome, rows[0].Trigger, rows[0].By));
        Assert.Equal((FiscalOutcome.Waiting, FiscalTrigger.Settle, "cashier", (int?)null), (rows[1].Outcome, rows[1].Trigger, rows[1].By, rows[1].HttpStatus));
        Assert.Contains("No such host", rows[1].Error);
        Assert.True(rows[1].HasPayload);   // what it tried to send is kept
    }

    [Fact]
    public async Task Practice_logs_the_json_a_real_send_would_carry_and_says_nothing_was_sent()
    {
        var b = await _db.Branches.FirstAsync();
        b.FiscalEnvironment = FiscalEnvironment.Practice;
        await _db.SaveChangesAsync();
        var inv = await Bill();
        await Svc(Office(HttpStatusCode.OK, Accepted)).Retry(inv.Id, "cashier");

        var row = Assert.Single((await Svc(Office(HttpStatusCode.OK, Accepted)).Log(null, false, null, null)).Rows);
        Assert.Equal((FiscalOutcome.Filed, "Practice"), (row.Outcome, row.Environment));
        var full = await Svc(Office(HttpStatusCode.OK, Accepted)).LogLine(row.Id);
        Assert.Equal("Practice: nothing was sent", full.Endpoint);
        Assert.Contains("\"USIN\":\"USIN000001\"", full.RequestBody);
        Assert.Null(full.ResponseBody);
    }

    [Fact]
    public async Task A_test_bill_is_logged_without_saving_a_bill()
    {
        var b = await _db.Branches.FirstAsync();
        await Svc(Office(HttpStatusCode.OK, Accepted)).SendTestInvoice(b.Id, "owner");
        Assert.Equal(0, await _db.Invoices.CountAsync());
        var row = Assert.Single((await Svc(Office(HttpStatusCode.OK, Accepted)).Log(null, false, null, null)).Rows);
        Assert.Equal((FiscalTrigger.Test, FiscalOutcome.Filed, (int?)null, "owner"), (row.Trigger, row.Outcome, row.InvoiceId, row.By));
        Assert.StartsWith("TEST", row.BillNumber);
    }

    [Fact]
    public async Task A_missing_token_is_logged_as_refused_and_not_sent()
    {
        var b = await _db.Branches.FirstAsync();
        b.FbrToken = null;
        await _db.SaveChangesAsync();
        var inv = await Bill();
        await Svc(Office(HttpStatusCode.OK, Accepted)).Retry(inv.Id, "cashier");
        var row = Assert.Single((await Svc(Office(HttpStatusCode.OK, Accepted)).Log(null, false, null, null)).Rows);
        Assert.Equal(FiscalOutcome.Refused, row.Outcome);
        Assert.False(row.HasPayload);
        Assert.StartsWith("Not sent:", (await Svc(Office(HttpStatusCode.OK, Accepted)).LogLine(row.Id)).Endpoint);
    }

    [Fact]
    public async Task A_crash_while_sending_is_logged_as_an_error_and_still_reported()
    {
        var inv = await Bill();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(new Broken()).Retry(inv.Id, "cashier"));
        var row = Assert.Single((await Svc(new Broken()).Log(null, false, null, null)).Rows);
        Assert.Equal(FiscalOutcome.Error, row.Outcome);
        Assert.Contains("disk on fire", row.Error);
        // The bill can still be sent afterwards: the claim was released.
        await Svc(Office(HttpStatusCode.OK, Accepted)).Retry(inv.Id, "cashier");
        Assert.Equal(FiscalStatus.Registered, (await _db.Invoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id)).FiscalStatus);
    }

    [Fact]
    public async Task Search_and_paging_work_and_another_restaurants_log_stays_hidden()
    {
        for (var i = 0; i < 3; i++) await Svc(Office(HttpStatusCode.OK, Accepted)).Retry((await Bill()).Id, "cashier");
        _db.FiscalLog.Add(new FiscalLogEntry { RestaurantId = 2, BillNumber = "OTHER-1", Office = "FBR", Outcome = FiscalOutcome.Filed });
        await _db.SaveChangesAsync();

        var svc = Svc(Office(HttpStatusCode.OK, Accepted));
        var first = await svc.Log(null, false, null, null, take: 2);
        Assert.Equal(2, first.Rows.Count);
        Assert.True(first.More);
        var rest = await svc.Log(null, false, null, first.Rows[^1].Id, take: 2);
        Assert.Single(rest.Rows);
        Assert.False(rest.More);
        Assert.DoesNotContain(first.Rows.Concat(rest.Rows), r => r.BillNumber == "OTHER-1");

        var number = (await _db.Invoices.AsNoTracking().OrderBy(i => i.Id).FirstAsync()).FormattedNumber;
        Assert.Single((await svc.Log(null, false, number, null)).Rows);
        await Assert.ThrowsAsync<DomainException>(() => svc.LogLine(_db.FiscalLog.IgnoreQueryFilters().First(x => x.RestaurantId == 2).Id));
    }

    [Fact]
    public async Task Lines_older_than_the_keep_period_are_cleared()
    {
        _db.FiscalLog.Add(new FiscalLogEntry { RestaurantId = 1, BillNumber = "OLD", Office = "FBR", Outcome = FiscalOutcome.Filed,
            At = DateTime.UtcNow.AddDays(-FiscalLogEntry.KeepDays - 1) });
        _db.FiscalLog.Add(new FiscalLogEntry { RestaurantId = 1, BillNumber = "NEW", Office = "FBR", Outcome = FiscalOutcome.Filed,
            At = DateTime.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(_opts, new StubTenant()));
        using var sp = services.BuildServiceProvider();
        var auto = new FiscalAutoRetryService(sp.GetRequiredService<IServiceScopeFactory>(), Options.Create(new FiscalOptions()),
            NullLogger<FiscalAutoRetryService>.Instance);

        Assert.Equal(1, await auto.PruneLog(default, now: true));
        Assert.Equal("NEW", (await _db.FiscalLog.AsNoTracking().SingleAsync()).BillNumber);
    }

    private sealed class Broken : IFiscalService
    {
        public Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default) =>
            throw new InvalidOperationException("disk on fire");
    }
}
