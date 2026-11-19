using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Auth;
using QrDine.Application.Common;

namespace QrDine.Infrastructure.Realtime;

/// <summary>
/// The live channel. Staff screens join the restaurant's "staff" group; each
/// customer's phone joins only its own table group, so a guest receives updates
/// for their table alone.
///
/// Nobody connects without a sign-in, and every join is checked: the restaurant-wide groups
/// (every order, the printers) are for that restaurant's signed-in staff only, and a table's
/// group is for that table's guests or the staff. The connection closes when its token runs out.
/// </summary>
[Authorize]
public class OrderHub : Hub
{
    private const string SignInAgain = "Please sign in again.";
    private readonly StaffSessions _sessions;
    private readonly LiveConnections _live;
    private readonly Persistence.AppDbContext _db;

    public OrderHub(StaffSessions sessions, LiveConnections live, Persistence.AppDbContext db)
    {
        _sessions = sessions; _live = live; _db = db;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _live.Left(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private async Task Add(string group)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        _live.Joined(Context.ConnectionId, Context.User!, group);
    }

    public static string StaffGroup(string slug) => $"staff:{slug}";
    // SignalR group names are case-sensitive, but table codes are compared
    // case-insensitively everywhere else. Canonicalise the code here so the
    // customer's JoinTable and the server's broadcast always land on the SAME
    // group — otherwise a casing/spacing mismatch silently drops the guest's
    // live updates and "order ready" bell.
    public static string TableGroup(string slug, string tableCode) =>
        $"table:{slug}:{(tableCode ?? string.Empty).Trim().ToUpperInvariant()}";
    public static string PrintGroup(string slug) => $"print:{slug}";

    // The group is named from the token's own restaurant, so it always matches the server's broadcasts.
    private string TokenSlug => Context.User?.FindFirst(AppClaims.Slug)?.Value ?? "";

    public async Task JoinStaff(string slug)
    {
        await RequireStaffOf(slug);
        await Add(StaffGroup(TokenSlug));
    }

    public async Task JoinTable(string slug, string tableCode)
    {
        if (!Access.MayJoinTableFeed(Context.User, slug, tableCode)) throw new HubException(SignInAgain);
        if (Access.IsStaff(Context.User)) await RequireStaffOf(slug);
        else await RequireGoodPass();
        await Add(TableGroup(TokenSlug, tableCode));
    }

    // A guest pass ends when the table's bill is settled: an old phone can't join the next guests' feed.
    private async Task RequireGoodPass()
    {
        var rid = int.TryParse(Context.User?.FindFirst(AppClaims.RestaurantId)?.Value, out var r) ? r : 0;
        var code = Context.User?.FindFirst(AppClaims.TableCode)?.Value ?? "";
        var table = await _db.Tables.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.RestaurantId == rid && t.Code == code);
        if (table is null || !table.PassStillGood(Access.IssuedAtOf(Context.User)))
            throw new HubException("passEnded");
    }

    public async Task JoinPrinters(string slug)
    {
        await RequireStaffOf(slug);
        await Add(PrintGroup(TokenSlug));
    }

    /// <summary>Broadcast a timer tick so all devices see the same remaining time. Staff only.</summary>
    public async Task TimerTick(string slug, string tableCode)
    {
        await RequireStaffOf(slug);
        await Clients.Group(TableGroup(TokenSlug, tableCode)).SendAsync("timerTick", DateTime.UtcNow);
    }

    private async Task RequireStaffOf(string slug)
    {
        if (!Access.MayJoinStaffFeed(Context.User, slug) || await _sessions.Check(Context.User) is null)
            throw new HubException(SignInAgain);
    }
}
