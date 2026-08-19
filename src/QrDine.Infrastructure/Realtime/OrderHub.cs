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
    public static string TableGroup(string slug, string tableCode) => $"table:{slug}:{tableCode}";
    public static string PrintGroup(string slug) => $"print:{slug}";

    public Task JoinStaff(string slug) => Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup(slug));
    public Task JoinTable(string slug, string tableCode) => Groups.AddToGroupAsync(Context.ConnectionId, TableGroup(slug, tableCode));
    public Task JoinPrinters(string slug) => Groups.AddToGroupAsync(Context.ConnectionId, PrintGroup(slug));
}
