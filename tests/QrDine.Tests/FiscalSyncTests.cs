using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// The no-duplicate-sync guarantee and error/retry behaviour, against a real
/// (in-memory SQLite) DbContext so the tenant filter + persistence are exercised.
/// </summary>
public class FiscalSyncTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();

    public FiscalSyncTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts, _tenant);
        _db.Database.EnsureCreated();
    }

    private async Task<Invoice> Seed(bool featureFbr = true, bool enabled = true, long? posId = 110014)
    {
        _db.Restaurants.Add(new Restaurant { Slug = "test", Name = "Test", FeatureFbr = featureFbr, FbrEnabled = enabled });
        await _db.SaveChangesAsync();
        _db.Branches.Add(new Branch { RestaurantId = 1, Name = "Main", FbrMode = FbrMode.Local, FbrPosId = posId });
        await _db.SaveChangesAsync();

        var line = new InvoiceLine { ItemCode = "1", Name = "Karahi", UnitPrice = 1000m, Qty = 1, TaxRate = 5m };
        var inv = Invoice.Create(1, 1, "T1", "Table 1", new[] { line }, 0, 0, "Cash", 1, "USIN000001");
        inv.BranchId = 1;
        inv.FiscalStatus = FiscalStatus.Pending;   // settled but not yet filed
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync();
        return inv;
    }

    [Fact]
    public async Task Retry_files_once_then_refuses_to_file_again()
    {
        var inv = await Seed();
        var fake = new FakeFiscal(new FiscalResult("FBR", FiscalStatus.Registered, "FBR-123", "FBR-123", null));
        var svc = new FiscalSyncService(_db, _tenant, fake);

        var dto = await svc.Retry(inv.Id, operatorUsername: null);
        Assert.Equal("Registered", dto.FiscalStatus);
        Assert.Equal("FBR-123", dto.FiscalNumber);
        Assert.Equal(1, fake.Calls);

        // The idempotency trigger: a second retry must NOT hit FBR again.
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.Retry(inv.Id, null));
        Assert.Contains("already filed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Failed_filing_records_error_and_stays_retryable()
    {
        var inv = await Seed();
        var fake = new FakeFiscal(new FiscalResult("FBR", FiscalStatus.Failed, null, null, "FBR rejected: bad NTN"));
        var svc = new FiscalSyncService(_db, _tenant, fake);

        var dto = await svc.Retry(inv.Id, null);
        Assert.Equal("Failed", dto.FiscalStatus);
        Assert.Equal("FBR rejected: bad NTN", dto.FiscalError);
        Assert.Equal(1, dto.FiscalAttempts);

        // Still not fiscalised → a retry is allowed and calls FBR again.
        var okFake = new FakeFiscal(new FiscalResult("FBR", FiscalStatus.Registered, "FBR-9", "FBR-9", null));
        var dto2 = await new FiscalSyncService(_db, _tenant, okFake).Retry(inv.Id, null);
        Assert.Equal("Registered", dto2.FiscalStatus);
        Assert.Equal(2, dto2.FiscalAttempts);
    }

    [Fact]
    public async Task Local_mode_retry_is_restricted_to_the_creating_cashier()
    {
        var inv = await Seed();
        inv.CreatedByUsername = "cash1";           // this invoice was settled by cash1
        await _db.SaveChangesAsync();
        var ok = new FiscalResult("FBR", FiscalStatus.Registered, "FBR-1", "FBR-1", null);

        // A different cashier cannot re-file it in local mode.
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new FiscalSyncService(_db, _tenant, new FakeFiscal(ok)).Retry(inv.Id, "cash2", isOwner: false));
        Assert.Contains("cash1", ex.Message);

        // The creator can.
        var dto = await new FiscalSyncService(_db, _tenant, new FakeFiscal(ok)).Retry(inv.Id, "cash1", isOwner: false);
        Assert.Equal("Registered", dto.FiscalStatus);
    }

    [Fact]
    public async Task Owner_can_override_local_creator_restriction()
    {
        var inv = await Seed();
        inv.CreatedByUsername = "cash1";
        await _db.SaveChangesAsync();
        var ok = new FiscalResult("FBR", FiscalStatus.Registered, "FBR-2", "FBR-2", null);

        var dto = await new FiscalSyncService(_db, _tenant, new FakeFiscal(ok)).Retry(inv.Id, "someone-else", isOwner: true);
        Assert.Equal("Registered", dto.FiscalStatus);
    }

    [Fact]
    public async Task SyncOnSettle_is_a_noop_when_fbr_disabled()
    {
        var inv = await Seed(enabled: false);
        var fake = new FakeFiscal(new FiscalResult("FBR", FiscalStatus.Registered, "X", "X", null));
        var r = await _db.Restaurants.FirstAsync();

        await new FiscalSyncService(_db, _tenant, fake).SyncOnSettle(inv, r, null);

        Assert.Equal(0, fake.Calls);
        Assert.Equal(FiscalStatus.NotRequired, inv.FiscalStatus);
    }

    [Fact]
    public async Task Retry_pending_appears_in_pending_list_then_synced_list()
    {
        var inv = await Seed();
        Assert.Single(await new FiscalSyncService(_db, _tenant, new FakeFiscal(default!)).List(synced: false));

        var fake = new FakeFiscal(new FiscalResult("FBR", FiscalStatus.Registered, "FBR-7", "FBR-7", null));
        await new FiscalSyncService(_db, _tenant, fake).Retry(inv.Id, null);

        Assert.Empty(await new FiscalSyncService(_db, _tenant, fake).List(synced: false));
        Assert.Single(await new FiscalSyncService(_db, _tenant, fake).List(synced: true));
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private sealed class FakeFiscal : IFiscalService
    {
        private readonly FiscalResult _result;
        public int Calls;
        public FakeFiscal(FiscalResult result) => _result = result;
        public Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}

internal sealed class StubTenant : ICurrentTenant
{
    public int? RestaurantId { get; private set; } = 1;
    public string? Slug { get; private set; } = "test";
    public bool IsResolved => RestaurantId.HasValue;
    public void Set(int restaurantId, string slug) { RestaurantId = restaurantId; Slug = slug; }
}
