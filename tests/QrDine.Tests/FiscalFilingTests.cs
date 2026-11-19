using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QrDine.Application.Admin;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure.Fiscal;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// Filing with FBR and PRA: Practice / Test / Live per branch, sending at settle or later, resending one, the
/// picked ones or all, the automatic resend, and the database claim that stops a bill being filed twice.
/// The transport tests run the real HTTP code against a stand-in tax office, so the addresses, the token, PRA's
/// field names and the error wording are all checked.
/// </summary>
public class FiscalFilingTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();

    public FiscalFilingTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant);
        _db.Database.EnsureCreated();
        _db.Restaurants.Add(new Restaurant { Slug = "test", Name = "Test", FeatureFbr = true, FbrEnabled = true, Ntn = "1234567-8" });
        _db.SaveChanges();
        _db.Branches.Add(new Branch
        {
            RestaurantId = 1, Name = "Main", FbrMode = FbrMode.Cloud, FbrPosId = 110014, FbrToken = "secret-token-1234",
            FiscalAuthority = FiscalAuthority.FBR, FiscalEnvironment = FiscalEnvironment.Test,
        });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private int _n;
    private async Task<Invoice> Bill(decimal price = 1000, decimal tax = 16, string method = "Cash", FiscalStatus status = FiscalStatus.Pending)
    {
        _n++;
        var line = new InvoiceLine { ItemCode = "1", Name = "Karahi", PctCode = "9801.0000", UnitPrice = price, Qty = 1, TaxRate = tax };
        var inv = Invoice.Create(1, _n, "T1", "Table 1", new[] { line }, 0, 0, method, _n, $"USIN{_n:D6}");
        inv.BranchId = 1;
        inv.FiscalStatus = status;
        inv.CreatedByUsername = "cashier";
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync();
        return inv;
    }

    private FiscalSyncService Svc(IFiscalService fiscal, bool forcedPractice = false) =>
        new(_db, _tenant, fiscal, new DefaultFiscalCapabilities { ForcedPractice = forcedPractice });

    private static FiscalResult Ok(string n = "FBR-1") => new("FBR", FiscalStatus.Registered, n, n, null, "Test");
    private static FiscalResult Offline() => new("FBR", FiscalStatus.Pending, null, null, "Couldn't reach FBR", "Test", Unreachable: true);
    private static FiscalResult No() => new("FBR", FiscalStatus.Failed, null, null, "FBR refused it", "Test");

    // ───────────────────────── when bills are sent ─────────────────────────

    [Fact]
    public async Task With_send_at_settle_off_a_bill_waits_and_is_sent_later_with_the_rest()
    {
        var r = await _db.Restaurants.FirstAsync();
        r.FiscalSendAtSettle = false;
        await _db.SaveChangesAsync();
        var fake = new Fake(Ok());
        var a = await Bill(); var b = await Bill();
        await Svc(fake).SyncOnSettle(a, r, "cashier"); await Svc(fake).SyncOnSettle(b, r, "cashier");
        await _db.SaveChangesAsync();

        Assert.Equal(0, fake.Calls);
        Assert.Equal(Invoice.WaitingToSend, (await _db.Invoices.AsNoTracking().FirstAsync(i => i.Id == a.Id)).FiscalError);
        Assert.Equal(2, (await Svc(fake).List(synced: false)).Count);

        var res = await Svc(fake).RetryMany(null, "cashier");
        Assert.Equal((2, 0, 0), (res.Sent, res.Refused, res.Left));
        Assert.Equal(2, fake.Calls);
        Assert.Equal(2, (await Svc(fake).List(synced: true)).Count);
    }

    [Fact]
    public async Task A_bill_refused_for_a_missing_pct_code_takes_the_code_from_the_menu_when_sent_again()
    {
        _db.Categories.Add(new QrDine.Domain.Menu.MenuCategory { Name = "Food" });
        await _db.SaveChangesAsync();
        var dish = new QrDine.Domain.Menu.MenuItem { CategoryId = _db.Categories.First().Id, Name = "Karahi", Price = 1000, PctCode = "9801.0000" };
        _db.MenuItems.Add(dish);
        await _db.SaveChangesAsync();
        var inv = await Bill(status: FiscalStatus.Failed);
        inv.Lines[0].ItemCode = dish.Id.ToString();
        inv.Lines[0].PctCode = "";
        await _db.SaveChangesAsync();

        string? sent = null;
        var fake = new Fake(Ok());
        fake.Seen = i => sent = i.Lines[0].PctCode;
        await Svc(fake).Retry(inv.Id, "cashier");
        Assert.Equal("9801.0000", sent);
        var saved = await _db.Invoices.AsNoTracking().Include(i => i.Lines).FirstAsync(i => i.Id == inv.Id);
        Assert.Equal(1160, saved.Total);   // the bill itself is as settled
    }

    [Fact]
    public async Task Sending_the_picked_bills_sends_only_those()
    {
        var fake = new Fake(Ok());
        var a = await Bill(); var b = await Bill(); var c = await Bill();
        var res = await Svc(fake).RetryMany(new[] { a.Id, c.Id }, "cashier");
        Assert.Equal(2, res.Sent);
        Assert.Equal(1, res.Left);
        Assert.Equal(FiscalStatus.Pending, (await _db.Invoices.AsNoTracking().FirstAsync(i => i.Id == b.Id)).FiscalStatus);
    }

    [Fact]
    public async Task Sending_all_stops_when_the_tax_office_cant_be_reached()
    {
        var fake = new Fake(Offline());
        for (var i = 0; i < 6; i++) await Bill();
        var res = await Svc(fake).RetryMany(null, "cashier");
        Assert.Equal(3, fake.Calls);
        Assert.Equal(3, res.StillWaiting);
        Assert.NotNull(res.Stopped);
        Assert.Equal(6, res.Left);
    }

    [Fact]
    public async Task A_bill_is_never_filed_twice_even_when_two_sends_overlap()
    {
        var inv = await Bill();
        FiscalSyncService? second = null;
        var nestedRefused = false;
        // While the first send is waiting for the tax office, a second send of the same bill starts.
        var fake = new Fake(Ok(), async () =>
        {
            try { await second!.Retry(inv.Id, "cashier"); }
            catch (DomainException e) when (e.Message.Contains("being sent")) { nestedRefused = true; }
        });
        second = Svc(fake);
        await Svc(fake).Retry(inv.Id, "cashier");

        Assert.True(nestedRefused);
        Assert.Equal(1, fake.Calls);
        var saved = await _db.Invoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
        Assert.Equal(FiscalStatus.Registered, saved.FiscalStatus);
        Assert.Null(saved.FiscalClaimUntil);   // the claim is let go afterwards
        await Assert.ThrowsAsync<DomainException>(() => Svc(fake).Retry(inv.Id, "cashier"));   // filed: never again
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task A_send_that_crashed_frees_the_bill_after_two_minutes()
    {
        var inv = await Bill();
        Assert.True(await _db.ClaimInvoiceForFiling(inv.Id, DateTime.UtcNow.AddMinutes(2)));
        Assert.False(await _db.ClaimInvoiceForFiling(inv.Id, DateTime.UtcNow.AddMinutes(2)));
        await _db.Invoices.Where(i => i.Id == inv.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.FiscalClaimUntil, DateTime.UtcNow.AddSeconds(-1)));
        Assert.True(await _db.ClaimInvoiceForFiling(inv.Id, DateTime.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public async Task The_automatic_resend_waits_longer_each_time_and_gives_up_on_refusals()
    {
        var offline = new Fake(Offline());
        var waiting = await Bill();
        Assert.Equal(0, await Svc(offline).AutoRetry());   // tried once: still offline
        Assert.Equal(1, offline.Calls);
        Assert.Equal(0, await Svc(offline).AutoRetry());   // just tried: not due again yet
        Assert.Equal(1, offline.Calls);

        await _db.Invoices.Where(i => i.Id == waiting.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.FiscalLastAttemptAt, DateTime.UtcNow.AddMinutes(-3)));
        Assert.Equal(1, await Svc(new Fake(Ok())).AutoRetry());   // due, and the office is back

        var refused = await Bill(status: FiscalStatus.Failed);
        await _db.Invoices.Where(i => i.Id == refused.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.FiscalAttempts, FiscalSyncService.MaxAutoTriesForRefused));
        var never = new Fake(Ok());
        Assert.Equal(0, await Svc(never).AutoRetry());
        Assert.Equal(0, never.Calls);

        var r = await _db.Restaurants.FirstAsync();
        r.FiscalAutoRetry = false;
        await _db.SaveChangesAsync();
        await Bill();
        Assert.Equal(0, await Svc(never).AutoRetry());   // switched off by the owner
    }

    [Fact]
    public async Task A_server_in_practice_mode_never_sends_whatever_a_branch_says()
    {
        var fake = new Fake(Ok());
        var inv = await Bill();
        await Svc(fake, forcedPractice: true).Retry(inv.Id, "cashier");
        Assert.Equal(FiscalEnvironment.Practice, fake.LastEnvironment);
        var check = Assert.Single(await Svc(fake, forcedPractice: true).Check());
        Assert.True(check.ForcedPractice);
        Assert.Equal("Practice", check.Environment);
    }

    // ───────────────────────── settings ─────────────────────────

    [Fact]
    public async Task Live_needs_the_posid_and_token_and_a_test_bill_never_goes_to_live()
    {
        var admin = new RestaurantAdminService(_db, new NoHasher(), _tenant);
        var id = (await _db.Branches.FirstAsync()).Id;
        var ex = await Assert.ThrowsAsync<DomainException>(() => admin.UpdateBranch(id,
            new BranchInput("Main", "", true, "Cloud", false, null, "", null, null, "PRA", "Live")));
        Assert.Contains("POSID", ex.Message);
        ex = await Assert.ThrowsAsync<DomainException>(() => admin.UpdateBranch(id,
            new BranchInput("Main", "", true, "Cloud", false, 77, "", null, null, "PRA", "Live")));
        Assert.Contains("token", ex.Message);

        _db.ChangeTracker.Clear();
        await admin.UpdateBranch(id, new BranchInput("Main", "", true, "Local", false, 77, "live-token-9876", null, null, "PRA", "Live"));
        var b = await _db.Branches.AsNoTracking().FirstAsync();
        Assert.Equal((FiscalAuthority.PRA, FiscalEnvironment.Live, FbrMode.Cloud, false), (b.FiscalAuthority, b.FiscalEnvironment, b.FbrMode, b.FbrSandbox));   // PRA: online only

        await Assert.ThrowsAsync<DomainException>(() => Svc(new Fake(Ok())).SendTestInvoice(id, null));

        // The saved token never goes back to a screen; leaving it out keeps it.
        var listed = Assert.Single(await admin.ListBranches());
        Assert.Null(listed.FbrToken);
        Assert.True(listed.HasFbrToken);
        Assert.Equal("9876", listed.TokenEnds);
        await admin.UpdateBranch(id, new BranchInput("Main 2", "", true, null, false, 77, null, null, null, "PRA", "Live"));
        Assert.Equal("live-token-9876", (await _db.Branches.AsNoTracking().FirstAsync()).FbrToken);
    }

    [Fact]
    public async Task The_check_says_what_is_missing_in_plain_words()
    {
        var b = await _db.Branches.FirstAsync();
        b.FbrToken = null; b.FbrPosId = null;
        await _db.SaveChangesAsync();
        var c = Assert.Single(await Svc(new Fake(Ok())).Check());
        Assert.False(c.Ready);
        Assert.Contains(c.Problems, p => p.Contains("POSID"));
        Assert.Contains(c.Problems, p => p.Contains("token"));

        b.FbrMode = FbrMode.Local; b.FbrPosId = 5;
        await _db.SaveChangesAsync();
        c = Assert.Single(await Svc(new Fake(Ok())).Check());
        Assert.Contains(c.Problems, p => p.Contains("PC edition"));   // the online server can't reach a PC's FBR software
    }

    [Fact]
    public async Task A_test_bill_proves_the_test_setup_without_saving_anything()
    {
        var fake = new Fake(Ok("SANDBOX-1"));
        var res = await Svc(fake).SendTestInvoice((await _db.Branches.FirstAsync()).Id, null);
        Assert.True(res.Accepted);
        Assert.Equal("SANDBOX-1", res.Number);
        Assert.Equal(FiscalEnvironment.Test, fake.LastEnvironment);
        Assert.Equal(0, await _db.Invoices.CountAsync());
    }

    // ───────────────────────── the transport, against a stand-in tax office ─────────────────────────

    private static FbrFiscalService Transport(StandIn office, bool simulate = false, bool allowLocal = false) =>
        new(new HttpClient(office), Options.Create(new FiscalOptions { Simulate = simulate, AllowLocal = allowLocal }),
            NullLogger<FbrFiscalService>.Instance);

    private static FiscalContext Ctx(FiscalAuthority office, FiscalEnvironment env, FbrMode mode = FbrMode.Cloud, string? token = "tok-1") =>
        new("Test", "1234567-8", "", mode, env != FiscalEnvironment.Live, null, 110014, token, office, env);

    private static Invoice TestBill()
    {
        var lines = new[]
        {
            new InvoiceLine { ItemCode = "1", Name = "Karahi", PctCode = "9801.0000", UnitPrice = 1200, Qty = 2, TaxRate = 16 },
            new InvoiceLine { ItemCode = "2", Name = "Naan", PctCode = "1905.9090", UnitPrice = 40, Qty = 3, TaxRate = 16 },
        };
        var inv = Invoice.Create(1, 1, "T1", "Table 1", lines, 0, 100, "Card", 7, "USIN000007");
        inv.BuyerNtn = "7654321-0";
        return inv;
    }

    [Fact]
    public async Task Fbr_test_goes_to_the_sandbox_with_the_token_and_correct_sums()
    {
        var office = new StandIn(HttpStatusCode.OK, """{"InvoiceNumber":"110014ABC","Code":"100","Response":"Fiscal Invoice Number generated successfully."}""");
        var res = await Transport(office).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Test));

        Assert.Equal(FiscalStatus.Registered, res.Status);
        Assert.Equal("110014ABC", res.Number);
        Assert.Equal("110014ABC", res.QrPayload);
        Assert.Equal("https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData", office.Url);
        Assert.Equal("Bearer tok-1", office.Auth);
        var body = office.Json!;
        Assert.Equal("7654321-0", (string?)body["BuyerNTN"]);
        Assert.Equal(2, (int)body["PaymentMode"]!);
        StandIn.CheckSums(body);
    }

    [Fact]
    public async Task Fbr_live_goes_to_the_production_gateway()
    {
        var office = new StandIn(HttpStatusCode.OK, """{"InvoiceNumber":"X1","Code":"100"}""");
        await Transport(office).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Live));
        Assert.Equal("https://gw.fbr.gov.pk/imsp/v1/api/Live/PostData", office.Url);
    }

    [Fact]
    public async Task Pra_uses_its_own_addresses_field_name_and_qr_link()
    {
        var office = new StandIn(HttpStatusCode.OK, """{"InvoiceNumber":"PRA-778899","Code":"100","Response":"Invoice received"}""");
        var res = await Transport(office).Register(TestBill(), Ctx(FiscalAuthority.PRA, FiscalEnvironment.Test));
        Assert.Equal("https://ims.pral.com.pk/ims/sandbox/api/Live/PostData", office.Url);
        Assert.Equal("PRA", res.Provider);
        Assert.Equal("https://reg.pra.punjab.gov.pk/IMSFiscalReport/SearchPOSInvoice_Report.aspx?PRAInvNo=PRA-778899", res.QrPayload);
        var body = office.Json!;
        Assert.Equal("7654321-0", (string?)body["BuyerPNTN"]);
        Assert.Null(body["BuyerNTN"]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$", (string)body["DateTime"]!);
        StandIn.CheckSums(body);

        await Transport(office).Register(TestBill(), Ctx(FiscalAuthority.PRA, FiscalEnvironment.Live));
        Assert.Equal("https://ims.pral.com.pk/ims/production/api/Live/PostData", office.Url);
    }

    [Fact]
    public async Task Refusals_and_outages_are_told_apart_in_plain_words()
    {
        var denied = await Transport(new StandIn(HttpStatusCode.Unauthorized, """{"fault":{"code":900908}}""")).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Test));
        Assert.Equal(FiscalStatus.Failed, denied.Status);
        Assert.Contains("didn't accept the token", denied.Error);

        var rejected = await Transport(new StandIn(HttpStatusCode.OK, """{"Code":"401","Errors":"Invalid PCT code"}""")).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Test));
        Assert.Equal(FiscalStatus.Failed, rejected.Status);
        Assert.Contains("Invalid PCT code", rejected.Error);

        var down = await Transport(new StandIn(HttpStatusCode.BadGateway, "bad gateway")).Register(TestBill(), Ctx(FiscalAuthority.PRA, FiscalEnvironment.Live));
        Assert.Equal(FiscalStatus.Pending, down.Status);
        Assert.True(down.Unreachable);

        var offline = await Transport(new StandIn(throws: true)).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Test));
        Assert.Equal(FiscalStatus.Pending, offline.Status);
        Assert.True(offline.Unreachable);
    }

    [Fact]
    public async Task Nothing_is_sent_without_a_token_in_practice_or_from_the_server_to_a_pc()
    {
        var office = new StandIn(HttpStatusCode.OK, """{"InvoiceNumber":"X","Code":"100"}""");
        var noToken = await Transport(office).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Test, token: null));
        Assert.Contains("token", noToken.Error);
        var local = await Transport(office).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Test, FbrMode.Local));
        Assert.Contains("PC edition", local.Error);
        var practice = await Transport(office).Register(TestBill(), Ctx(FiscalAuthority.PRA, FiscalEnvironment.Practice));
        Assert.StartsWith("PRACTICE-", practice.Number);
        var forced = await Transport(office, simulate: true).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Live));
        Assert.Equal("Practice", forced.Environment);
        Assert.Null(office.Url);   // none of these reached the office

        await Transport(office, allowLocal: true).Register(TestBill(), Ctx(FiscalAuthority.FBR, FiscalEnvironment.Test, FbrMode.Local, token: null));
        Assert.Equal("http://localhost:8524/api/IMSFiscal/GetInvoiceNumberByModel", office.Url);   // the PC edition's FBR software
        Assert.Null(office.Auth);
    }

    // ───────────────────────── stand-ins ─────────────────────────

    private sealed class NoHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string hash) => password == hash;
    }

    private sealed class Fake(FiscalResult result, Func<Task>? during = null) : IFiscalService
    {
        public int Calls;
        public FiscalEnvironment? LastEnvironment;
        public Action<Invoice>? Seen;
        public async Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default)
        {
            Calls++;
            LastEnvironment = ctx.Environment;
            Seen?.Invoke(invoice);
            if (during is not null) await during();
            return ctx.Environment == FiscalEnvironment.Practice ? result with { Environment = "Practice" } : result;
        }
    }

    /// <summary>A stand-in tax office: records what it was sent and answers as told.</summary>
    internal sealed class StandIn(HttpStatusCode status = HttpStatusCode.OK, string answer = "", bool throws = false) : HttpMessageHandler
    {
        public string? Url;
        public string? Auth;
        public JsonObject? Json;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (throws) throw new HttpRequestException("No such host is known.");
            Url = request.RequestUri!.ToString();
            Auth = request.Headers.Authorization?.ToString();
            Json = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            return new HttpResponseMessage(status) { Content = new StringContent(answer) };
        }

        /// <summary>The arithmetic the tax office checks: every line adds up, and the bill is the sum of its lines.</summary>
        public static void CheckSums(JsonObject b)
        {
            var items = b["Items"]!.AsArray().Select(i => i!.AsObject()).ToList();
            foreach (var i in items)
            {
                var sale = (double)i["SaleValue"]!; var tax = (double)i["TaxCharged"]!; var rate = (double)i["TaxRate"]!;
                var disc = (double)i["Discount"]!; var further = (double)i["FurtherTax"]!;
                Near((sale - disc) * rate / 100, tax);
                Near(sale - disc + tax + further, (double)i["TotalAmount"]!);
            }
            Near(items.Sum(i => (double)i["SaleValue"]!), (double)b["TotalSaleValue"]!);
            Near(items.Sum(i => (double)i["TaxCharged"]!), (double)b["TotalTaxCharged"]!);
            Near(items.Sum(i => (double)i["Quantity"]!), (double)b["TotalQuantity"]!);
            Near(items.Sum(i => (double)i["Discount"]!), (double)b["Discount"]!);
            Near((double)b["TotalSaleValue"]! - (double)b["Discount"]! + (double)b["TotalTaxCharged"]! + (double)b["FurtherTax"]!,
                (double)b["TotalBillAmount"]!);
        }

        // Within a paisa (rounding to 2 places can differ by one in the last place).
        private static void Near(double expected, double actual) =>
            Assert.True(Math.Abs(expected - actual) <= 0.0101, $"expected {expected}, got {actual}");
    }
}
