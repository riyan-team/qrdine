using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Infrastructure.Realtime;

/// <summary>
/// Who is listening to which live group. SignalR can't list a group's members, so the hub records each join
/// here. That lets the server drop phones whose table pass ended (the bill was settled) at once, and lets
/// <see cref="LiveFeedGuard"/> check every connection again, so a removed staff member or a changed password
/// stops the live orders too, not only the next request.
/// </summary>
public sealed class LiveConnections
{
    private sealed class Conn
    {
        public required ClaimsPrincipal User { get; init; }
        public ConcurrentDictionary<string, byte> Groups { get; } = new();
    }

    private readonly ConcurrentDictionary<string, Conn> _conns = new();

    public void Joined(string connectionId, ClaimsPrincipal user, string group) =>
        _conns.GetOrAdd(connectionId, _ => new Conn { User = user }).Groups[group] = 0;

    public void Left(string connectionId) => _conns.TryRemove(connectionId, out _);

    public void LeftGroup(string connectionId, string group)
    {
        if (_conns.TryGetValue(connectionId, out var c)) c.Groups.TryRemove(group, out _);
    }

    public int Count => _conns.Count;

    /// <summary>Every connection with its sign-in and groups, as they are now.</summary>
    public List<(string ConnectionId, ClaimsPrincipal User, string[] Groups)> Snapshot() =>
        _conns.Select(kv => (kv.Key, kv.Value.User, kv.Value.Groups.Keys.ToArray())).ToList();

    /// <summary>The guest phones in one group.</summary>
    public List<string> GuestsIn(string group) =>
        _conns.Where(kv => kv.Value.Groups.ContainsKey(group) && Access.IsGuest(kv.Value.User)).Select(kv => kv.Key).ToList();
}

/// <summary>
/// Every minute, checks each live connection again: a staff sign-in must still match a staff member with the
/// same password (and so the same restaurant), and a guest pass must still be good for its table. Anything
/// that fails is taken out of its groups and told why, so it gets no more orders.
/// </summary>
public sealed class LiveFeedGuard : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
    private readonly LiveConnections _live;
    private readonly IHubContext<OrderHub> _hub;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LiveFeedGuard> _log;

    public LiveFeedGuard(LiveConnections live, IHubContext<OrderHub> hub, IServiceScopeFactory scopes, ILogger<LiveFeedGuard> log)
    {
        _live = live; _hub = hub; _scopes = scopes; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stop))
        {
            try { await CheckAll(stop); }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Live feed check failed; trying again in a minute.");
            }
        }
    }

    /// <summary>One pass over every connection. Public so tests can run it without waiting.</summary>
    public async Task<int> CheckAll(CancellationToken ct = default)
    {
        var all = _live.Snapshot();
        if (all.Count == 0) return 0;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<StaffSessions>();
        var dropped = 0;
        foreach (var (id, user, groups) in all)
        {
            string? why = null;
            if (Access.IsStaff(user))
            {
                if (await sessions.Check(user, ct) is null) why = "signedOut";
            }
            else if (Access.IsGuest(user))
            {
                var rid = int.TryParse(user.FindFirst(AppClaims.RestaurantId)?.Value, out var r) ? r : 0;
                var code = user.FindFirst(AppClaims.TableCode)?.Value ?? "";
                var table = await db.Tables.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(t => t.RestaurantId == rid && t.Code == code, ct);
                if (table is null || !table.PassStillGood(Access.IssuedAtOf(user))) why = "passEnded";
            }
            if (why is null) continue;
            foreach (var g in groups)
            {
                await _hub.Groups.RemoveFromGroupAsync(id, g, ct);
                _live.LeftGroup(id, g);
            }
            await _hub.Clients.Client(id).SendAsync(why, ct);
            dropped++;
        }
        return dropped;
    }
}
