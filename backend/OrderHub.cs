using Microsoft.AspNetCore.SignalR;

namespace QrDine.Api;

// SignalR = the live nerve system. This is the reason to stay on .NET.
// Staff screens join the "staff" group for a restaurant; each table joins its
// own group so a customer's phone only receives updates for their own orders.
public class OrderHub : Hub
{
    public static string StaffGroup(string slug) => $"staff:{slug}";
    public static string TableGroup(string slug, string tableCode) => $"table:{slug}:{tableCode}";

    public async Task JoinStaff(string slug)
        => await Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup(slug));

    public async Task JoinTable(string slug, string tableCode)
        => await Groups.AddToGroupAsync(Context.ConnectionId, TableGroup(slug, tableCode));
}
