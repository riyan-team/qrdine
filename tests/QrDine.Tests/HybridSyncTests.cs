using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QrDine.Api.Auth;
using QrDine.Api.Endpoints;
using QrDine.Api.Middleware;
using QrDine.Api.Pc;
using QrDine.Application;
using QrDine.Application.Common;
using QrDine.Application.Sync;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Customers;
using QrDine.Domain.Expenses;
using QrDine.Domain.Files;
using QrDine.Domain.Identity;
using QrDine.Domain.Inventory;
using QrDine.Domain.Loyalty;
using QrDine.Domain.Menu;
using QrDine.Domain.Ordering;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;
using QrDine.Domain.Tables;
using QrDine.Infrastructure;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using QrDine.Infrastructure.Sync;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// Hybrid, end to end over real HTTP: a server with a restaurant, and a PC that connects to it, downloads it,
/// takes over the counter, sells while the server's owner edits the menu from home, loses the internet, and
/// catches up. Both sides are real databases (SQLite files); the PC's sync is the same <see cref="PcSync"/>
/// that runs inside DineIn.exe.
/// </summary>
public sealed class HybridSyncTests : IAsyncLifetime
{
    private const int Cafe = 1;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"qrdine-hybrid-{Guid.NewGuid():N}");
    private WebApplication _server = null!;
    private ServiceProvider _pc = null!;
    private PcSync _sync = null!;
    private readonly Switchable _net = new();
    private string _key = "";

    /// <summary>The PC's line to the server, with a switch to pull the internet out.</summary>
    private sealed class Switchable : DelegatingHandler
    {
        public bool Offline { get; set; }
        public Switchable() : base(new HttpClientHandler()) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Offline ? throw new HttpRequestException("No route to host") : base.SendAsync(request, ct);
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        var secret = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());

        // ---- the server ----
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Logging.ClearProviders();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(_dir, "server.db")}",
            ["Jwt:Secret"] = secret,
            ["Sync:Enabled"] = "true",
            ["Sync:SettleSeconds"] = "0",
        });
        b.Services.AddApplication();
        b.Services.AddInfrastructure(b.Configuration);
        b.Services.AddQrDineAuth(b.Configuration);
        _server = b.Build();
        using (var scope = _server.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        _server.UseMiddleware<ExceptionHandlingMiddleware>();
        _server.UseAuthentication();
        _server.UseMiddleware<TenantResolutionMiddleware>();
        _server.UseAuthorization();
        _server.MapFiles();
        _server.MapSync();
        await _server.StartAsync();

        // ---- the PC (what DineIn.exe registers, without the web host) ----
        var pcConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(_dir, "pc.db")}",
            ["Jwt:Secret"] = secret,
            ["Sync:Enabled"] = "false",
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(pcConfig);
        s.AddApplication();
        s.AddInfrastructure(pcConfig);
        var paths = new PcPaths(_dir, Path.Combine(_dir, "pc.db"), Path.Combine(_dir, "backups"), Path.Combine(_dir, "licence.key"),
            Path.Combine(_dir, "clock.txt"), Path.Combine(_dir, "settings.json"), 5100);
        s.AddSingleton(paths);
        s.AddSingleton(new Application.Pc.LicenceVerifier(PcLicence.SellerPublicKey));
        s.AddSingleton(sp => new PcLicence(paths, sp.GetRequiredService<Application.Pc.LicenceVerifier>(), "HYBRID-TEST-PC"));
        s.AddSingleton<HttpMessageHandler>(_net);
        s.AddSingleton<PcSync>();
        _pc = s.BuildServiceProvider();
        using (var scope = _pc.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        _sync = _pc.GetRequiredService<PcSync>();

        await SeedServer();
        _key = (await OnServer(sp => sp.GetRequiredService<SyncService>().Pair("Counter PC", "owner"))).Key;
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        await _server.DisposeAsync();
        await _pc.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string ServerUrl => _server.Urls.First();

    // ---------------------------------------------------------------- helpers

    private async Task<T> OnServer<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _server.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenant>().Set(Cafe, "cafe");
        return await work(scope.ServiceProvider);
    }

    private async Task<T> OnPc<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _pc.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var r = await db.Restaurants.IgnoreQueryFilters().FirstOrDefaultAsync();
        if (r is not null) scope.ServiceProvider.GetRequiredService<CurrentTenant>().Set(r.Id, r.Slug);
        return await work(db);
    }

    private Task<T> ServerDb<T>(Func<AppDbContext, Task<T>> work) => OnServer(sp => work(sp.GetRequiredService<AppDbContext>()));

    private static Guid Sid(AppDbContext db, object row) => (Guid)db.Entry(row).Property(SyncModel.SyncIdProperty).CurrentValue!;

    private static async Task<T> BySid<T>(AppDbContext db, Guid sid) where T : class =>
        await db.Set<T>().FirstAsync(e => EF.Property<Guid>(e, SyncModel.SyncIdProperty) == sid);

    private async Task Connect()
    {
        await _sync.Connect(ServerUrl, _key, default);
        await _sync.RoundOnce(default);
        var status = await _sync.Status();
        Assert.True(status.State == PcSyncStages.Ready, $"not ready: {status.LastError}");
    }

    private async Task SeedServer() => await ServerDb(async db =>
    {
        db.Restaurants.Add(new Restaurant { Slug = "cafe", Name = "Cafe", TaxEnabled = true, TaxPercent = 16, ReceiptFooter = "Thank you" });
        await db.SaveChangesAsync();
        var branch = new Branch { RestaurantId = Cafe, Name = "Main", FbrToken = "server-branch-token" };
        db.Branches.Add(branch);
        var rice = new StockItem { RestaurantId = Cafe, Name = "Rice", Unit = "kg", Quantity = 50, LowThreshold = 5 };
        db.Set<StockItem>().Add(rice);
        var mains = new MenuCategory { RestaurantId = Cafe, Name = "Mains" };
        db.Categories.Add(mains);
        db.Staff.Add(new StaffMember
        {
            RestaurantId = Cafe, Username = "03001234567", DisplayName = "Owner", Role = StaffRole.Owner,
            PasswordHash = new Infrastructure.Identity.Pbkdf2PasswordHasher().Hash("first-password"), FbrToken = "server-staff-token",
        });
        var ali = new Customer { RestaurantId = Cafe, Name = "Ali", Phone = "03111111111" };
        db.Set<Customer>().Add(ali);
        db.Set<LoyaltyProgram>().Add(new LoyaltyProgram { RestaurantId = Cafe });
        var member = new LoyaltyMember { RestaurantId = Cafe, Name = "Sara", Phone = "03222222222", Points = 20, Token = "member-phone-pass" };
        db.Set<LoyaltyMember>().Add(member);
        db.Set<StoredFile>().Add(new StoredFile { Id = "0123456789abcdef0123456789abcdef", ContentType = "image/png", Data = new byte[] { 1, 2, 3 }, RestaurantId = Cafe });
        await db.SaveChangesAsync();

        db.Tables.AddRange(
            new DiningTable { RestaurantId = Cafe, BranchId = branch.Id, Code = "T1", Name = "Table 1", QrKey = "k1" },
            new DiningTable { RestaurantId = Cafe, BranchId = branch.Id, Code = "T2", Name = "Table 2", QrKey = "k2" });
        var biryani = new MenuItem
        {
            RestaurantId = Cafe, CategoryId = mains.Id, Name = "Biryani", Price = 500, ImageUrl = "/api/files/0123456789abcdef0123456789abcdef",
            Variants = { new MenuItemVariant { Name = "Half", Price = 300 }, new MenuItemVariant { Name = "Full", Price = 500 } },
            ModifierGroups = { new ModifierGroup { Name = "Spice", Options = { new ModifierOption { Name = "Mild" }, new ModifierOption { Name = "Hot", Price = 20 } } } },
            Recipe = { new RecipeLine { StockItemId = rice.Id, QtyPerServing = 0.25m } },
        };
        db.MenuItems.Add(biryani);
        await db.SaveChangesAsync();

        // Yesterday's bill, made online before the PC existed.
        var t1 = await db.Tables.FirstAsync(t => t.Code == "T1");
        var session = new TableSession { RestaurantId = Cafe, BranchId = branch.Id, DiningTableId = t1.Id, TableCode = "T1", TableName = "Table 1", IsOpen = false, CustomerId = ali.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var order = Order.Place(Cafe, new TableSessionInfo(session.Id, t1.Id, "T1", "Table 1", branch.Id, "Main"), null);
        order.AddItem(biryani.Id, "Biryani", "Full", 500, 2, null, new[] { ("Hot", 20m) });
        db.Orders.Add(order);
        var bill = new Invoice
        {
            RestaurantId = Cafe, BranchId = branch.Id, TableSessionId = session.Id, Number = 1, Usin = "USIN000001", TableCode = "T1", TableName = "Table 1",
            Subtotal = 1040, TaxAmount = 166.4m, Total = 1206.4m, Lines = { new InvoiceLine { Name = "Biryani", Qty = 2, UnitPrice = 520, SaleValue = 1040, TaxRate = 16, TaxAmount = 166.4m, TotalAmount = 1206.4m } },
        };
        db.Invoices.Add(bill);
        await db.SaveChangesAsync();
        db.Set<CustomerEntry>().Add(new CustomerEntry { RestaurantId = Cafe, CustomerId = ali.Id, InvoiceId = bill.Id, Type = CustomerEntryType.Sale, Amount = 1206.4m, Date = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return 0;
    });

    // ---------------------------------------------------------------- the story

    [Fact]
    public async Task A_pc_downloads_its_restaurant_with_every_link_intact_and_takes_over_the_counter()
    {
        // Before the PC takes over, the server still sells.
        await ServerDb(async db => { db.Set<Expense>().Add(new Expense { RestaurantId = Cafe, Category = "Gas", Amount = 900, Date = DateTime.UtcNow }); return await db.SaveChangesAsync(); });

        await Connect();

        // Every row arrived, matched by its SyncId.
        var serverCounts = await ServerDb(async db => await Counts(db));
        var pcCounts = await OnPc(async db => await Counts(db));
        Assert.Equal(serverCounts, pcCounts);

        await OnPc(async db =>
        {
            var r = await db.Restaurants.SingleAsync();
            Assert.Equal("cafe", r.Slug);
            Assert.Equal("Thank you", r.ReceiptFooter);
            Assert.Equal(16, r.TaxPercent);

            var biryani = await db.MenuItems.Include(m => m.Variants).Include(m => m.ModifierGroups).ThenInclude(g => g.Options).Include(m => m.Recipe).SingleAsync();
            var mains = await db.Categories.SingleAsync();
            var rice = await db.Set<StockItem>().SingleAsync();
            Assert.Equal(mains.Id, biryani.CategoryId);                       // the link points at this PC's own row
            Assert.Equal(new[] { "Full", "Half" }, biryani.Variants.Select(v => v.Name).OrderBy(x => x));
            Assert.Equal(2, biryani.ModifierGroups.Single().Options.Count);
            Assert.Equal(rice.Id, biryani.Recipe.Single().StockItemId);
            Assert.Equal(50, rice.Quantity);                                   // the first download takes the counts too

            var bill = await db.Invoices.Include(i => i.Lines).SingleAsync();
            var session = await db.Sessions.SingleAsync();
            var ali = await db.Set<Customer>().SingleAsync();
            Assert.Equal(1, bill.Number);
            Assert.Equal(1206.4m, bill.Total);
            Assert.Single(bill.Lines);
            Assert.Equal(session.Id, bill.TableSessionId);
            Assert.Equal(ali.Id, session.CustomerId);
            Assert.Equal(bill.Id, (await db.Set<CustomerEntry>().SingleAsync()).InvoiceId);
            var order = await db.Orders.Include(o => o.Items).ThenInclude(i => i.Modifiers).SingleAsync();
            Assert.Equal(biryani.Id, order.Items.Single().MenuItemId);
            Assert.Equal("Hot", order.Items.Single().Modifiers.Single().Name);

            // Staff sign in here with their own password; FBR tokens and phone passes stay where they were entered.
            var owner = await db.Staff.SingleAsync();
            Assert.True(new Infrastructure.Identity.Pbkdf2PasswordHasher().Verify("first-password", owner.PasswordHash));
            Assert.True(string.IsNullOrEmpty(owner.FbrToken));
            Assert.True(string.IsNullOrEmpty((await db.Branches.SingleAsync()).FbrToken));
            var member = await db.Set<LoyaltyMember>().SingleAsync();
            Assert.Equal(20, member.Points);
            Assert.True(string.IsNullOrEmpty(member.Token));
            Assert.True(await db.Set<StoredFile>().AnyAsync(f => f.Id == "0123456789abcdef0123456789abcdef"));   // the menu photo
            return 0;
        });

        // The server now refuses to make floor records itself, and to change the counts the PC keeps.
        var refused = await Assert.ThrowsAsync<DomainException>(() => ServerDb(async db =>
        {
            db.Set<Expense>().Add(new Expense { RestaurantId = Cafe, Category = "Gas", Amount = 100, Date = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        }));
        Assert.Contains("runs on its own PC", refused.Message);
        await Assert.ThrowsAsync<DomainException>(() => ServerDb(async db =>
        {
            (await db.Set<StockItem>().SingleAsync()).Quantity = 999;
            return await db.SaveChangesAsync();
        }));
        // Settings still change from home.
        await ServerDb(async db => { (await db.MenuItems.SingleAsync()).Price = 550; return await db.SaveChangesAsync(); });

        // A PC can't connect twice, and the one-device rule holds.
        await Assert.ThrowsAsync<DomainException>(() => _sync.Connect(ServerUrl, _key, default));
    }

    [Fact]
    public async Task The_pc_sells_the_owner_edits_from_home_and_both_end_up_the_same()
    {
        await Connect();

        // ---- at the counter (the PC) ----
        await OnPc(async db =>
        {
            var r = await db.Restaurants.SingleAsync();
            var branch = await db.Branches.SingleAsync();
            var t2 = await db.Tables.FirstAsync(t => t.Code == "T2");
            var biryani = await db.MenuItems.SingleAsync();
            var rice = await db.Set<StockItem>().SingleAsync();
            var member = await db.Set<LoyaltyMember>().SingleAsync();
            var ali = await db.Set<Customer>().SingleAsync();

            var session = new TableSession { RestaurantId = r.Id, BranchId = branch.Id, DiningTableId = t2.Id, TableCode = "T2", TableName = "Table 2", LoyaltyMemberId = member.Id, CustomerId = ali.Id };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
            var order = Order.Place(r.Id, new TableSessionInfo(session.Id, t2.Id, "T2", "Table 2", branch.Id, "Main"), "no onions");
            order.AddItem(biryani.Id, "Biryani", "Half", 300, 1, null);
            db.Orders.Add(order);
            var bill = new Invoice
            {
                RestaurantId = r.Id, BranchId = branch.Id, TableSessionId = session.Id, Number = 2, Usin = "USIN000002", TableCode = "T2", TableName = "Table 2",
                Subtotal = 300, TaxAmount = 48, Total = 348, Lines = { new InvoiceLine { Name = "Biryani (Half)", Qty = 1, UnitPrice = 300, SaleValue = 300, TaxRate = 16, TaxAmount = 48, TotalAmount = 348 } },
            };
            db.Invoices.Add(bill);
            await db.SaveChangesAsync();
            db.Set<Payment>().Add(new Payment { RestaurantId = r.Id, BranchId = branch.Id, TableSessionId = session.Id, Amount = 348, Method = "Cash" });
            db.Set<StockMovement>().Add(new StockMovement { RestaurantId = r.Id, StockItemId = rice.Id, Delta = -0.125m, Type = StockMovementType.Sale });
            rice.Quantity -= 0.125m;
            db.Set<LoyaltyTransaction>().Add(new LoyaltyTransaction { RestaurantId = r.Id, MemberId = member.Id, InvoiceId = bill.Id, TableSessionId = session.Id, Points = 3 });
            member.Points += 3;
            db.Set<CustomerEntry>().Add(new CustomerEntry { RestaurantId = r.Id, CustomerId = ali.Id, InvoiceId = bill.Id, Type = CustomerEntryType.Sale, Amount = 348, Date = DateTime.UtcNow });
            t2.Name = "Terrace";
            return await db.SaveChangesAsync();
        });

        await Task.Delay(20);
        // ---- meanwhile, the owner at home (the server), after the PC's changes ----
        await ServerDb(async db =>
        {
            (await db.MenuItems.SingleAsync()).Price = 550;
            (await db.Set<StockItem>().SingleAsync()).Name = "Basmati rice";
            return await db.SaveChangesAsync();
        });

        await _sync.RoundOnce(default);
        var st = await _sync.Status();
        Assert.True(st.Online, st.LastError);
        Assert.Equal(0, st.Waiting);
        Assert.Equal(0, st.ServerFailed);

        // The server has the PC's sale, every link turned into the server's own rows.
        await ServerDb(async db =>
        {
            Assert.Empty(await db.SyncInbox.Where(x => x.Status == SyncInboxStatus.Failed).Select(x => x.EntityType + ": " + x.Note).ToListAsync());
            var bill = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Number == 2);
            var branch = await db.Branches.SingleAsync();
            var t2 = await db.Tables.FirstAsync(t => t.Code == "T2");
            var session = await db.Sessions.SingleAsync(s => s.Id == bill.TableSessionId);
            var biryani = await db.MenuItems.SingleAsync();
            var rice = await db.Set<StockItem>().SingleAsync();
            var member = await db.Set<LoyaltyMember>().SingleAsync();
            var ali = await db.Set<Customer>().SingleAsync();
            Assert.Equal(348, bill.Total);
            Assert.Single(bill.Lines);
            Assert.Equal(branch.Id, bill.BranchId);
            Assert.Equal(t2.Id, session.DiningTableId);
            Assert.Equal(member.Id, session.LoyaltyMemberId);
            Assert.Equal(ali.Id, session.CustomerId);
            var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.TableSessionId == session.Id);
            Assert.Equal(biryani.Id, order.Items.Single().MenuItemId);
            Assert.Equal("no onions", order.CustomerNote);
            Assert.Equal(348, (await db.Set<Payment>().SingleAsync()).Amount);
            Assert.Equal(rice.Id, (await db.Set<StockMovement>().SingleAsync()).StockItemId);
            var points = await db.Set<LoyaltyTransaction>().SingleAsync();
            Assert.Equal((member.Id, bill.Id), (points.MemberId, points.InvoiceId));
            Assert.Equal(bill.Id, (await db.Set<CustomerEntry>().SingleAsync(e => e.Amount == 348)).InvoiceId);

            Assert.Equal("Basmati rice", rice.Name);       // the owner's later edit stays
            Assert.Equal(49.875m, rice.Quantity);           // and the PC's count is the count
            Assert.Equal(23, member.Points);
            Assert.Equal("Terrace", t2.Name);               // the PC's newer edit
            Assert.Equal(550, biryani.Price);
            return 0;
        });

        // The PC has the owner's edits, and its own counts.
        await OnPc(async db =>
        {
            Assert.Equal(550, (await db.MenuItems.SingleAsync()).Price);
            var rice = await db.Set<StockItem>().SingleAsync();
            Assert.Equal(("Basmati rice", 49.875m), (rice.Name, rice.Quantity));
            Assert.Equal("Terrace", (await db.Tables.FirstAsync(t => t.Code == "T2")).Name);
            return 0;
        });

        // Nothing bounces back and forth: more rounds change nothing on either side.
        var before = (await ServerDb(db => db.SyncChanges.CountAsync()), await OnPc(db => db.SyncChanges.IgnoreQueryFilters().CountAsync()));
        await _sync.RoundOnce(default);
        await _sync.RoundOnce(default);
        var after = (await ServerDb(db => db.SyncChanges.CountAsync()), await OnPc(db => db.SyncChanges.IgnoreQueryFilters().CountAsync()));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task The_newest_change_wins_both_ways_and_removals_travel()
    {
        await Connect();

        // The PC renames, then the owner renames later: the owner's name wins on both sides.
        await OnPc(async db => { (await db.MenuItems.SingleAsync()).Name = "Chicken biryani"; return await db.SaveChangesAsync(); });
        await Task.Delay(20);
        await ServerDb(async db => { (await db.MenuItems.SingleAsync()).Name = "Mutton biryani"; return await db.SaveChangesAsync(); });
        await _sync.RoundOnce(default);
        Assert.Equal("Mutton biryani", await ServerDb(async db => (await db.MenuItems.SingleAsync()).Name));
        Assert.Equal("Mutton biryani", await OnPc(async db => (await db.MenuItems.SingleAsync()).Name));

        // The other way round: the PC's later rename wins.
        await ServerDb(async db => { (await db.MenuItems.SingleAsync()).Name = "Beef biryani"; return await db.SaveChangesAsync(); });
        await Task.Delay(20);
        await OnPc(async db => { (await db.MenuItems.SingleAsync()).Name = "Sindhi biryani"; return await db.SaveChangesAsync(); });
        await _sync.RoundOnce(default);
        Assert.Equal("Sindhi biryani", await ServerDb(async db => (await db.MenuItems.SingleAsync()).Name));
        Assert.Equal("Sindhi biryani", await OnPc(async db => (await db.MenuItems.SingleAsync()).Name));

        // A category added online reaches the PC, and its removal follows it.
        await ServerDb(async db => { db.Categories.Add(new MenuCategory { RestaurantId = Cafe, Name = "Drinks" }); return await db.SaveChangesAsync(); });
        await _sync.RoundOnce(default);
        Assert.True(await OnPc(db => db.Categories.AnyAsync(c => c.Name == "Drinks")));
        await ServerDb(async db => { db.Categories.Remove(await db.Categories.SingleAsync(c => c.Name == "Drinks")); return await db.SaveChangesAsync(); });
        await _sync.RoundOnce(default);
        Assert.False(await OnPc(db => db.Categories.AnyAsync(c => c.Name == "Drinks")));

        // A customer added and removed at the counter goes the same way up.
        await OnPc(async db => { db.Set<Customer>().Add(new Customer { RestaurantId = (await db.Restaurants.SingleAsync()).Id, Name = "Walk-in Bilal" }); return await db.SaveChangesAsync(); });
        await _sync.RoundOnce(default);
        Assert.True(await ServerDb(db => db.Set<Customer>().AnyAsync(c => c.Name == "Walk-in Bilal")));
        await OnPc(async db => { db.Set<Customer>().Remove(await db.Set<Customer>().SingleAsync(c => c.Name == "Walk-in Bilal")); return await db.SaveChangesAsync(); });
        await _sync.RoundOnce(default);
        Assert.False(await ServerDb(db => db.Set<Customer>().AnyAsync(c => c.Name == "Walk-in Bilal")));

        // A password changed online works at the counter on the next round.
        await ServerDb(async db => { (await db.Staff.SingleAsync()).PasswordHash = new Infrastructure.Identity.Pbkdf2PasswordHasher().Hash("second-password"); return await db.SaveChangesAsync(); });
        await _sync.RoundOnce(default);
        Assert.True(await OnPc(async db => new Infrastructure.Identity.Pbkdf2PasswordHasher().Verify("second-password", (await db.Staff.SingleAsync()).PasswordHash)));
    }

    [Fact]
    public async Task With_the_internet_out_the_pc_keeps_selling_and_sends_everything_when_it_is_back()
    {
        await Connect();
        _net.Offline = true;

        for (var n = 2; n <= 6; n++)
        {
            var number = n;
            await OnPc(async db =>
            {
                var r = await db.Restaurants.SingleAsync();
                var branch = await db.Branches.SingleAsync();
                var t1 = await db.Tables.FirstAsync(t => t.Code == "T1");
                var session = new TableSession { RestaurantId = r.Id, BranchId = branch.Id, DiningTableId = t1.Id, TableCode = "T1", TableName = "Table 1", IsOpen = false };
                db.Sessions.Add(session);
                await db.SaveChangesAsync();
                db.Invoices.Add(new Invoice { RestaurantId = r.Id, BranchId = branch.Id, TableSessionId = session.Id, Number = number, Usin = $"USIN{number:D6}", Total = 100 * number, TableCode = "T1" });
                return await db.SaveChangesAsync();
            });
            await _sync.RoundOnce(default);
        }
        var offline = await _sync.Status();
        Assert.False(offline.Online);
        Assert.Contains("keeps working", offline.LastError);
        Assert.True(offline.Waiting >= 10);   // five sessions and five bills
        Assert.Equal(1, await ServerDb(db => db.Invoices.CountAsync()));

        _net.Offline = false;
        await _sync.RoundOnce(default);
        var back = await _sync.Status();
        Assert.True(back.Online, back.LastError);
        Assert.Equal(0, back.Waiting);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, await ServerDb(db => db.Invoices.OrderBy(i => i.Number).Select(i => i.Number).ToListAsync()));
        Assert.Equal(100m * (2 + 3 + 4 + 5 + 6) + 1206.4m, await ServerDb(async db => (await db.Invoices.ToListAsync()).Sum(i => i.Total)));
    }

    private Task<int> PcBill(int number) => OnPc(async db =>
    {
        var r = await db.Restaurants.SingleAsync();
        var branch = await db.Branches.SingleAsync();
        var t1 = await db.Tables.FirstAsync(t => t.Code == "T1");
        var session = new TableSession { RestaurantId = r.Id, BranchId = branch.Id, DiningTableId = t1.Id, TableCode = "T1", TableName = "Table 1", IsOpen = false };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        db.Invoices.Add(new Invoice { RestaurantId = r.Id, BranchId = branch.Id, TableSessionId = session.Id, Number = number, Usin = $"USIN{number:D6}", Total = 100 * number, TableCode = "T1" });
        return await db.SaveChangesAsync();
    });

    private Task RemoveThePcOnline() => OnServer(async sp =>
    {
        var svc = sp.GetRequiredService<SyncService>();
        foreach (var d in (await svc.Devices()).Where(d => d.RevokedAt is null)) await svc.Revoke(d.Id);
        return 0;
    });

    [Fact]
    public async Task A_pc_removed_online_keeps_working_and_a_new_key_for_the_same_restaurant_reconnects_it()
    {
        await Connect();
        await RemoveThePcOnline();
        await PcBill(2);
        await _sync.RoundOnce(default);
        var st = await _sync.Status();
        Assert.True(st.KeyRefused);
        Assert.Contains("removed online", st.LastError);
        Assert.True(st.Waiting > 0);                                       // the bill waits on the PC
        Assert.Equal(1, await ServerDb(db => db.Invoices.CountAsync()));

        var key2 = (await OnServer(sp => sp.GetRequiredService<SyncService>().Pair("Counter PC", "owner"))).Key;
        Assert.Contains("isn't paired", (await Assert.ThrowsAsync<DomainException>(() => _sync.Connect(ServerUrl, "dk_made-up", default))).Message);
        Assert.Contains("connected to", (await Assert.ThrowsAsync<DomainException>(() => _sync.Connect("https://elsewhere.example", key2, default))).Message);
        await _sync.Connect(ServerUrl, key2, default);
        await _sync.RoundOnce(default);

        st = await _sync.Status();
        Assert.False(st.KeyRefused, st.LastError);
        Assert.Equal(0, st.Waiting);
        Assert.Equal(new[] { 1, 2 }, await ServerDb(db => db.Invoices.OrderBy(i => i.Number).Select(i => i.Number).ToListAsync()));
        Assert.NotNull(await ServerDb(async db => (await db.SyncDevices.SingleAsync(d => d.RevokedAt == null)).CounterSince));
        // The online copy stepped back again.
        await Assert.ThrowsAsync<DomainException>(() => ServerDb(async db =>
        {
            db.Set<Expense>().Add(new Expense { RestaurantId = Cafe, Category = "Gas", Amount = 100, Date = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task A_removed_pc_cannot_take_the_counter_back_once_bills_were_made_online_meanwhile()
    {
        await Connect();
        await RemoveThePcOnline();
        // With no PC, the online copy takes the counter again: a bill made online now would clash with the PC's numbers.
        await ServerDb(async db => { db.Set<Expense>().Add(new Expense { RestaurantId = Cafe, Category = "Gas", Amount = 100, Date = DateTime.UtcNow }); return await db.SaveChangesAsync(); });
        var key2 = (await OnServer(sp => sp.GetRequiredService<SyncService>().Pair("Counter PC", "owner"))).Key;
        var refused = await Assert.ThrowsAsync<DomainException>(() => _sync.Connect(ServerUrl, key2, default));
        Assert.Contains("fresh PC", refused.Message);
        Assert.Null(await ServerDb(async db => (await db.SyncDevices.SingleAsync(d => d.RevokedAt == null)).CounterSince));
        Assert.True((await _sync.Status()).State == PcSyncStages.Ready);   // the PC itself carries on as it was
    }

    [Fact]
    public async Task A_wrong_key_or_address_is_refused_before_anything_is_written()
    {
        var bad = await Assert.ThrowsAsync<DomainException>(() => _sync.Connect(ServerUrl, "dk_not-a-real-key", default));
        Assert.Contains("isn't paired", bad.Message);
        await Assert.ThrowsAsync<DomainException>(() => _sync.Connect("ftp://example", _key, default));
        await Assert.ThrowsAsync<DomainException>(() => _sync.Connect(ServerUrl, "not-a-key", default));
        Assert.Equal("off", (await _sync.Status()).State);
        Assert.False(File.Exists(Path.Combine(_dir, "sync.json")));
        Assert.Null(await ServerDb(async db => (await db.SyncDevices.SingleAsync()).CounterSince));
    }

    private static async Task<Dictionary<string, int>> Counts(AppDbContext db)
    {
        var store = new EfSyncStore(db);
        var counts = new Dictionary<string, int>();
        foreach (var type in store.RootTypes)
        {
            var rows = 0;
            var after = 0;
            while (true)
            {
                var page = await store.Page(type, (await db.Restaurants.IgnoreQueryFilters().FirstAsync()).Id, after, 500);
                rows += page.Count;
                if (page.Count < 500) break;
                after = page[^1].Id;
            }
            counts[type] = rows;
        }
        return counts;
    }
}
