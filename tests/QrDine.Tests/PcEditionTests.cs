using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QrDine.Api.Pc;
using QrDine.Application.Pc;
using QrDine.Domain.Billing;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure.Pc;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// The PC edition: licence keys (genuine, for this PC, dates, grace, a clock moved back), what an ended licence
/// still allows, and the SQLite upgrade that adds what a newer version needs without losing a row.
/// </summary>
public class PcEditionTests : IDisposable
{
    private readonly ECDsa _seller = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly LicenceVerifier _verifier;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dinein-pc-test-" + Guid.NewGuid().ToString("N"));
    private const string Pc = "ABCD-EFGH-JKLM-NPQR";
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

    public PcEditionTests()
    {
        _verifier = new LicenceVerifier(Convert.ToBase64String(_seller.ExportSubjectPublicKeyInfo()));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        _seller.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Key(DateOnly? until, string machine = Pc, ECDsa? by = null, string[]? features = null) =>
        LicenceIssuer.Issue(by ?? _seller, new LicenceInfo("lic1", "Karachi Grill", machine, new DateOnly(2026, 11, 1), until, features ?? LicenceVerifier.AllFeatures));

    // ---- licence keys ----

    [Fact]
    public void A_genuine_licence_for_this_pc_works_and_says_until_when()
    {
        var s = _verifier.Evaluate(Key(new DateOnly(2027, 11, 20)), Pc, Now);
        Assert.Equal(LicenceState.Active, s.State);
        Assert.True(s.CanWork);
        Assert.Equal("Karachi Grill", s.Licence!.Restaurant);
        Assert.Equal(366, s.DaysLeft);
        Assert.Contains("fbr", s.Licence.Features);
    }

    [Fact]
    public void A_key_pasted_with_spaces_and_line_breaks_and_a_lower_case_code_still_works()
    {
        var k = Key(null);
        var pasted = "  " + k[..20] + "\r\n" + k[20..40] + " \n" + k[40..] + "\n";
        Assert.Equal(LicenceState.Active, _verifier.Evaluate(pasted, "abcd efgh jklm npqr", Now).State);
    }

    [Fact]
    public void A_changed_key_or_one_from_another_seller_is_refused()
    {
        var k = Key(null);
        var payload = k.Split('.')[1];
        var forged = k.Replace(payload, payload[..^2] + (payload[^2] == 'A' ? "B" : "A") + payload[^1]);
        Assert.Equal(LicenceState.Invalid, _verifier.Evaluate(forged, Pc, Now).State);

        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(LicenceState.Invalid, _verifier.Evaluate(Key(null, by: other), Pc, Now).State);
        Assert.Equal(LicenceState.Invalid, _verifier.Evaluate("hello", Pc, Now).State);
        Assert.Equal(LicenceState.Missing, _verifier.Evaluate("  ", Pc, Now).State);
    }

    [Fact]
    public void A_licence_for_another_pc_says_this_pcs_code()
    {
        var s = _verifier.Evaluate(Key(null, machine: "ZZZZ-YYYY-XXXX-WWWW"), Pc, Now);
        Assert.Equal(LicenceState.WrongComputer, s.State);
        Assert.False(s.CanRead);
        Assert.Contains(Pc, s.Message);
    }

    [Fact]
    public void Near_the_end_it_reminds_then_works_a_week_longer_then_only_reads()
    {
        var k = Key(new DateOnly(2026, 11, 30));
        var soon = _verifier.Evaluate(k, Pc, Now);
        Assert.Equal((LicenceState.EndingSoon, 11), (soon.State, soon.DaysLeft));

        // Works through its last day (until 5 am the next morning in Pakistan), then a week of grace.
        Assert.Equal(LicenceState.EndingSoon, _verifier.Evaluate(k, Pc, new DateTime(2026, 11, 30, 23, 59, 0, DateTimeKind.Utc)).State);
        var grace = _verifier.Evaluate(k, Pc, new DateTime(2026, 12, 2, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal(LicenceState.Grace, grace.State);
        Assert.True(grace.CanWork);
        Assert.Equal(6, grace.DaysLeft);

        var ended = _verifier.Evaluate(k, Pc, new DateTime(2026, 12, 8, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(LicenceState.Expired, ended.State);
        Assert.False(ended.CanWork);
        Assert.True(ended.CanRead);
    }

    [Fact]
    public void Moving_the_clock_back_does_not_bring_an_ended_licence_back()
    {
        var k = Key(new DateOnly(2026, 11, 30));
        var s = _verifier.Evaluate(k, Pc, Now, lastSeenUtc: new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(LicenceState.ClockBack, s.State);
        Assert.False(s.CanWork);
        // A few hours off (time zones, a slow clock) is fine.
        Assert.Equal(LicenceState.EndingSoon, _verifier.Evaluate(k, Pc, Now, lastSeenUtc: Now.AddHours(10)).State);
    }

    [Fact]
    public void A_machine_code_is_four_groups_of_four_and_the_same_every_time()
    {
        var a = MachineCodeText.FromId("6b1f2c9e-1111-2222-3333-444455556666");
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", a);
        Assert.Equal(a, MachineCodeText.FromId("6B1F2C9E-1111-2222-3333-444455556666 "));
        Assert.NotEqual(a, MachineCodeText.FromId("6b1f2c9e-1111-2222-3333-444455556667"));
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){3}$", MachineCode.ForThisComputer());
    }

    // ---- the licence on the PC ----

    private PcPaths Paths() => new(_dir, Path.Combine(_dir, "dinein.db"), Path.Combine(_dir, "backups"),
        Path.Combine(_dir, "licence.key"), Path.Combine(_dir, "clock.txt"), Path.Combine(_dir, "dinein-settings.json"), 5100);

    [Fact]
    public void Entering_a_licence_saves_it_only_when_it_works_here()
    {
        var clock = new FakeTimeProvider(Now);
        var l = new PcLicence(Paths(), _verifier, Pc, clock);
        Assert.Equal(LicenceState.Missing, l.Status.State);

        Assert.Equal(LicenceState.WrongComputer, l.Activate(Key(null, machine: "ZZZZ-YYYY-XXXX-WWWW")).State);
        Assert.Equal(LicenceState.Expired, l.Activate(Key(new DateOnly(2026, 1, 1))).State);
        Assert.False(File.Exists(Paths().LicenceFile));
        Assert.Equal(LicenceState.Missing, l.Status.State);

        Assert.True(l.Activate(Key(new DateOnly(2027, 11, 20))).CanWork);
        Assert.True(File.Exists(Paths().LicenceFile));
        // A restart reads it back.
        Assert.Equal(LicenceState.Active, new PcLicence(Paths(), _verifier, Pc, clock).Status.State);
    }

    [Fact]
    public void The_pc_remembers_the_latest_time_it_ran_so_a_restart_with_the_clock_back_is_caught()
    {
        var clock = new FakeTimeProvider(Now);
        var l = new PcLicence(Paths(), _verifier, Pc, clock);
        l.Activate(Key(new DateOnly(2027, 11, 20)));
        clock.Advance(TimeSpan.FromDays(30));
        Assert.Equal(LicenceState.Active, l.Status.State);   // re-checked after 5 minutes, which saves the time

        var back = new FakeTimeProvider(Now);
        Assert.Equal(LicenceState.ClockBack, new PcLicence(Paths(), _verifier, Pc, back).Status.State);

        // Bills dated later than the clock count too, even with the clock file gone.
        File.Delete(Paths().ClockFile);
        var fresh = new PcLicence(Paths(), _verifier, Pc, back);
        Assert.Equal(LicenceState.Active, fresh.Status.State);
        fresh.SawRecordsUpTo(Now.AddDays(20));
        Assert.Equal(LicenceState.ClockBack, fresh.Status.State);
    }

    // ---- what the app allows ----

    private async Task<int> Gate(PcLicence l, string method, string path, AppDbContext db)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        var passed = false;
        await new LicenceGateMiddleware(_ => { passed = true; return Task.CompletedTask; }, l).InvokeAsync(ctx, db);
        return passed ? 0 : ctx.Response.StatusCode;
    }

    [Fact]
    public async Task Without_a_licence_only_the_licence_screen_works_and_an_ended_one_still_reads()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options, new StubTenant());
        db.Database.EnsureCreated();
        var clock = new FakeTimeProvider(Now);
        var l = new PcLicence(Paths(), _verifier, Pc, clock);

        Assert.Equal(402, await Gate(l, "GET", "/api/r/grill/orders", db));
        Assert.Equal(0, await Gate(l, "GET", "/api/licence", db));
        Assert.Equal(0, await Gate(l, "POST", "/api/licence", db));
        Assert.Equal(0, await Gate(l, "GET", "/login", db));   // the web pages themselves

        Assert.True(l.Activate(Key(new DateOnly(2026, 11, 25))).CanWork);
        Assert.Equal(0, await Gate(l, "POST", "/api/r/grill/orders", db));
        Assert.Equal(0, await Gate(l, "POST", "/api/onboard", db));

        // Past the grace week: reading and signing in still work, making anything new doesn't.
        clock.Advance(TimeSpan.FromDays(14));
        Assert.Equal(0, await Gate(l, "GET", "/api/r/grill/admin/reports/today", db));
        Assert.Equal(0, await Gate(l, "POST", "/api/r/grill/auth/login", db));
        Assert.Equal(402, await Gate(l, "POST", "/api/r/grill/orders", db));
        Assert.Equal(402, await Gate(l, "PUT", "/api/r/grill/admin/billing", db));
    }

    [Fact]
    public async Task The_pc_holds_one_restaurant_and_the_licence_sets_its_features()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options, new StubTenant());
        db.Database.EnsureCreated();
        db.Restaurants.Add(new Restaurant { Slug = "grill", Name = "Grill", FeatureFbr = false, FeatureInventory = true });
        await db.SaveChangesAsync();
        var l = new PcLicence(Paths(), _verifier, Pc, new FakeTimeProvider(Now));
        l.Activate(Key(new DateOnly(2027, 11, 20), features: ["ordering", "kitchen", "billing", "fbr"]));

        await PcLicenceApplier.Apply(db, l, NullLogger.Instance);
        var r = await db.Restaurants.IgnoreQueryFilters().AsNoTracking().FirstAsync();
        Assert.True(r.FeatureFbr && r.FeatureOrdering && r.FeatureKitchen && r.FeatureBilling);
        Assert.False(r.FeatureInventory || r.FeatureWaiter || r.FeatureReports);
        Assert.Equal(403, await Gate(l, "POST", "/api/onboard", db));
    }

    private sealed class FakeTimeProvider(DateTime start) : TimeProvider
    {
        private DateTimeOffset _now = new(start);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    // ---- the SQLite upgrade ----

    private AppDbContext FileDb(string file) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options, new StubTenant());

    private static void Sql(AppDbContext db, string sql) => db.Database.ExecuteSqlRaw(sql);

    [Fact]
    public async Task An_older_pc_database_gets_the_new_tables_columns_and_indexes_and_keeps_every_row()
    {
        var file = Path.Combine(_dir, "dinein.db");
        using (var db = FileDb(file))
        {
            var made = await SqliteSchemaUpgrader.UpgradeAsync(db, NullLogger.Instance);
            Assert.True(made.Created);
            db.Restaurants.Add(new Restaurant { Slug = "grill", Name = "Grill", OneTaxForAllItems = true });
            await db.SaveChangesAsync();
            db.Branches.Add(new Branch { RestaurantId = 1, Name = "Main" });
            await db.SaveChangesAsync();
            // Make it look like the database of an older version: these didn't exist yet.
            var syncIndex = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Branches' AND sql LIKE '%SyncId%'").ToList();
            foreach (var i in syncIndex) Sql(db, $"DROP INDEX \"{i}\"");
            Sql(db, "ALTER TABLE \"Restaurants\" DROP COLUMN \"OneTaxForAllItems\"");
            Sql(db, "ALTER TABLE \"Branches\" DROP COLUMN \"FiscalAuthority\"");
            Sql(db, "ALTER TABLE \"Branches\" DROP COLUMN \"SyncId\"");
            Sql(db, "DROP TABLE \"PaymentReturns\"");
            Sql(db, "DROP INDEX \"IX_Invoices_RestaurantId_FiscalStatus\"");
        }

        var backup = Path.Combine(_dir, "backups", "before-update.db");
        using (var db = FileDb(file))
        {
            var up = await SqliteSchemaUpgrader.UpgradeAsync(db, NullLogger.Instance, backup);
            Assert.False(up.Created);
            Assert.Contains("table PaymentReturns", up.Added);
            Assert.Contains("column Restaurants.OneTaxForAllItems", up.Added);
            Assert.Contains("column Branches.FiscalAuthority", up.Added);
            Assert.Contains("column Branches.SyncId", up.Added);
            Assert.Contains("index IX_Invoices_RestaurantId_FiscalStatus", up.Added);
            Assert.True(File.Exists(backup));
        }

        using (var db = FileDb(file))
        {
            var r = await db.Restaurants.IgnoreQueryFilters().SingleAsync();
            Assert.Equal("Grill", r.Name);
            Assert.False(r.OneTaxForAllItems);   // the model's default for a column that wasn't there
            var b = await db.Branches.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(FiscalAuthority.FBR, b.FiscalAuthority);
            var sync = db.Database.SqlQueryRaw<string>("SELECT \"SyncId\" AS Value FROM \"Branches\"").ToList();
            Assert.NotEqual("00000000-0000-0000-0000-000000000000", sync.Single());
            Assert.True(Guid.TryParse(sync.Single(), out _));
            // The new parts work.
            db.Branches.Add(new Branch { RestaurantId = 1, Name = "Second" });
            await db.SaveChangesAsync();
            Assert.Equal(0, await db.PaymentReturns.IgnoreQueryFilters().CountAsync());

            var again = await SqliteSchemaUpgrader.UpgradeAsync(db, NullLogger.Instance, Path.Combine(_dir, "backups", "second.db"));
            Assert.Empty(again.Added);
            Assert.False(File.Exists(Path.Combine(_dir, "backups", "second.db")));
        }
    }

    [Fact]
    public void A_backup_copies_the_database_and_keeps_the_last_few()
    {
        var file = Path.Combine(_dir, "dinein.db");
        using (var db = FileDb(file)) { db.Database.EnsureCreated(); db.Restaurants.Add(new Restaurant { Slug = "grill", Name = "Grill" }); db.SaveChanges(); }
        var folder = Path.Combine(_dir, "backups");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 5; i++) File.WriteAllText(Path.Combine(folder, $"dinein-2020-01-0{i + 1}-0000.db"), "old");
        var svc = new PcBackupService(new PcBackupOptions(file, folder, Path.Combine(_dir, "usb"), Keep: 3), NullLogger<PcBackupService>.Instance);

        var made = svc.BackupNow();
        Assert.Equal(2, made.Count);
        Assert.Equal(3, Directory.GetFiles(folder, "dinein-*.db").Length);
        using var copy = FileDb(made[1]);
        Assert.Equal("Grill", copy.Restaurants.IgnoreQueryFilters().Single().Name);
    }
}
