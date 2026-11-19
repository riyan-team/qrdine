using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QrDine.Api.Auth;
using QrDine.Api.Endpoints;
using QrDine.Api.Middleware;
using QrDine.Application;
using QrDine.Application.Sync;
using QrDine.Domain.Menu;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// The sync routes over real HTTP, with the same middleware order as Program.cs: switched off, every
/// sync address answers 404 (even a bad body or a valid key); switched on, a device key reaches only
/// its own restaurant, and a removed device or a suspended restaurant is refused.
/// </summary>
public class SyncHttpTests
{
    private sealed class Server : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Http { get; init; }
        public required string DbPath { get; init; }

        public async Task<T> In<T>(int restaurantId, Func<IServiceProvider, Task<T>> work)
        {
            using var scope = App.Services.CreateScope();
            var r = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Restaurants.FindAsync(restaurantId);
            scope.ServiceProvider.GetRequiredService<CurrentTenant>().Set(restaurantId, r!.Slug);
            return await work(scope.ServiceProvider);
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try { File.Delete(DbPath); } catch (IOException) { }
        }
    }

    private static async Task<Server> Start(bool syncOn)
    {
        var db = Path.Combine(Path.GetTempPath(), $"qrdine-sync-http-{Guid.NewGuid():N}.db");
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Logging.ClearProviders();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Default"] = $"Data Source={db}",
            ["Jwt:Secret"] = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            ["Sync:Enabled"] = syncOn ? "true" : "false",
            ["Sync:SettleSeconds"] = "0",
        });
        b.Services.AddApplication();
        b.Services.AddInfrastructure(b.Configuration);
        b.Services.AddQrDineAuth(b.Configuration);

        var app = b.Build();
        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.EnsureCreatedAsync();
            ctx.Restaurants.AddRange(new Restaurant { Slug = "cafe", Name = "Cafe" }, new Restaurant { Slug = "other", Name = "Other" });
            await ctx.SaveChangesAsync();
        }

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();
        app.MapSync();
        await app.StartAsync();
        return new Server { App = app, Http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) }, DbPath = db };
    }

    private static Task<PairedDevice> Pair(Server s, int restaurantId) =>
        s.In(restaurantId, sp => sp.GetRequiredService<SyncService>().Pair("Counter PC", "owner"));

    private static Task Dish(Server s, int restaurantId, string name) => s.In(restaurantId, async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var category = new MenuCategory { RestaurantId = restaurantId, Name = "Mains" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        db.MenuItems.Add(new MenuItem { RestaurantId = restaurantId, CategoryId = category.Id, Name = name, Price = 500 });
        return await db.SaveChangesAsync();
    });

    private static HttpRequestMessage Get(string url, string? key = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, url);
        if (key is not null) m.Headers.Add(SyncEndpoints.DeviceKeyHeader, key);
        return m;
    }

    private static HttpRequestMessage Push(string json, string? key)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, "/api/sync/push") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key is not null) m.Headers.Add(SyncEndpoints.DeviceKeyHeader, key);
        return m;
    }

    private static string OneChange(Guid changeId, string op = "Upsert") => JsonSerializer.Serialize(new
    {
        changes = new[] { new { changeId, type = "MenuItem", syncId = Guid.NewGuid(), op, changedAt = DateTime.UtcNow, data = new { name = "Pulao" } } },
    });

    [Fact]
    public async Task Switched_off_every_sync_address_answers_404()
    {
        await using var s = await Start(syncOn: false);
        var key = (await Pair(s, 1)).Key;   // even a real key gets nowhere while the switch is off

        var requests = new List<HttpRequestMessage>
        {
            Get("/api/sync/hello"),
            Get("/api/sync/hello", key),
            Get("/api/sync/snapshot?type=MenuItem", key),
            Get("/api/sync/pull?since=not-a-number", key),
            Push("{ this is not json", key),
            Push(OneChange(Guid.NewGuid()), key),
            Get("/api/r/cafe/admin/sync/devices"),
            new(HttpMethod.Post, "/api/r/cafe/admin/sync/devices") { Content = JsonContent.Create(new { name = "PC" }) },
            new(HttpMethod.Delete, "/api/r/cafe/admin/sync/devices/1"),
        };
        foreach (var req in requests)
        {
            var label = $"{req.Method} {req.RequestUri}";
            var res = await s.Http.SendAsync(req);
            Assert.True(res.StatusCode == HttpStatusCode.NotFound, $"{label} answered {(int)res.StatusCode}");
        }
        var stored = await s.In(1, sp => Task.FromResult(sp.GetRequiredService<AppDbContext>().SyncInbox.Count()));
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task Switched_on_a_device_key_reaches_only_its_own_restaurant()
    {
        await using var s = await Start(syncOn: true);
        await Dish(s, 1, "Biryani");
        await Dish(s, 2, "Other's karahi");
        var mine = await Pair(s, 1);

        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.SendAsync(Get("/api/sync/hello"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.SendAsync(Get("/api/sync/hello", "dk_not-a-real-key"))).StatusCode);
        // The owner's screens still need the owner's sign-in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.GetAsync("/api/r/cafe/admin/sync/devices")).StatusCode);

        var hello = await s.Http.SendAsync(Get("/api/sync/hello", mine.Key));
        Assert.Equal(HttpStatusCode.OK, hello.StatusCode);
        var h = JsonDocument.Parse(await hello.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, h.GetProperty("restaurantId").GetInt32());
        Assert.Equal("cafe", h.GetProperty("slug").GetString());

        var snap = JsonDocument.Parse(await (await s.Http.SendAsync(Get("/api/sync/snapshot?type=MenuItem", mine.Key))).Content.ReadAsStringAsync()).RootElement;
        var names = snap.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("data").GetProperty("Name").GetString()).ToList();
        Assert.Equal(new[] { "Biryani" }, names);

        var pull = JsonDocument.Parse(await (await s.Http.SendAsync(Get("/api/sync/pull?since=0", mine.Key))).Content.ReadAsStringAsync()).RootElement;
        var pulled = pull.GetProperty("changes").EnumerateArray().ToList();
        Assert.NotEmpty(pulled);
        Assert.DoesNotContain(pulled, c => c.GetProperty("data").ValueKind == JsonValueKind.Object
            && c.GetProperty("data").TryGetProperty("Name", out var n) && n.GetString() == "Other's karahi");
    }

    [Fact]
    public async Task Switched_on_push_is_stored_once_and_bad_changes_are_refused()
    {
        await using var s = await Start(syncOn: true);
        var key = (await Pair(s, 1)).Key;
        var change = Guid.NewGuid();

        var first = await s.Http.SendAsync(Push(OneChange(change), key));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<SyncPushResult>())!.Accepted);

        var again = await (await s.Http.SendAsync(Push(OneChange(change), key))).Content.ReadFromJsonAsync<SyncPushResult>();
        Assert.Equal((0, 1), (again!.Accepted, again.AlreadyHad));

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Http.SendAsync(Push(OneChange(Guid.NewGuid(), op: "Explode"), key))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.SendAsync(Push(OneChange(Guid.NewGuid()), "dk_wrong"))).StatusCode);

        var inbox = await s.In(1, sp => Task.FromResult(sp.GetRequiredService<AppDbContext>().SyncInbox.Select(x => x.RestaurantId).ToList()));
        Assert.Equal(new[] { 1 }, inbox);
    }

    [Fact]
    public async Task Switched_on_the_same_batch_sent_eight_times_at_once_is_stored_once()
    {
        await using var s = await Start(syncOn: true);
        var key = (await Pair(s, 1)).Key;
        var batch = OneChange(Guid.NewGuid());

        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var res = await s.Http.SendAsync(Push(batch, key));
            return (res.StatusCode, Result: res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<SyncPushResult>() : null);
        }));

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));   // they take turns, so none is refused
        Assert.Equal(1, answers.Sum(a => a.Result!.Accepted));
        Assert.Equal(7, answers.Sum(a => a.Result!.AlreadyHad));
        Assert.Equal(1, await s.In(1, sp => Task.FromResult(sp.GetRequiredService<AppDbContext>().SyncInbox.Count())));
    }

    [Fact]
    public async Task Switched_on_a_removed_device_or_a_suspended_restaurant_is_refused()
    {
        await using var s = await Start(syncOn: true);
        var a = await Pair(s, 1);
        var b = await Pair(s, 2);

        await s.In(1, async sp => { await sp.GetRequiredService<SyncService>().Revoke(a.Id); return 0; });
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.SendAsync(Get("/api/sync/hello", a.Key))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await s.Http.SendAsync(Get("/api/sync/hello", b.Key))).StatusCode);
        await s.In(2, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            (await db.Restaurants.FindAsync(2))!.Status = RestaurantStatus.Suspended;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Http.SendAsync(Get("/api/sync/hello", b.Key))).StatusCode);
    }
}
