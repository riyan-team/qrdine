using Microsoft.AspNetCore.SignalR;

namespace QrDine.Infrastructure.Realtime;

/// <summary>
/// The live channel. Staff screens join the restaurant's "staff" group; each
/// customer's phone joins only its own table group, so a guest receives updates
/// for their table alone.
/// </summary>
public class OrderHub : Hub
{
    public static string StaffGroup(string slug) => $"staff:{slug}";
    // SignalR group names are case-sensitive, but table codes are compared
    // case-insensitively everywhere else. Canonicalise the code here so the
    // customer's JoinTable and the server's broadcast always land on the SAME
    // group — otherwise a casing/spacing mismatch silently drops the guest's
    // live updates and "order ready" bell.
    public static string TableGroup(string slug, string tableCode) =>
        $"table:{slug}:{(tableCode ?? string.Empty).Trim().ToUpperInvariant()}";
    public static string PrintGroup(string slug) => $"print:{slug}";

    public Task JoinStaff(string slug) => Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup(slug));
    public Task JoinTable(string slug, string tableCode) => Groups.AddToGroupAsync(Context.ConnectionId, TableGroup(slug, tableCode));
    public Task JoinPrinters(string slug) => Groups.AddToGroupAsync(Context.ConnectionId, PrintGroup(slug));

    /// <summary>Broadcast a timer tick so all devices see the same remaining time.</summary>
    public async Task TimerTick(string slug, string tableCode)
    {
        await Clients.Group(TableGroup(slug, tableCode)).SendAsync("timerTick", DateTime.UtcNow);
    }
}
