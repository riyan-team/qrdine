using System.Security.Claims;
using QrDine.Application.Common;
using QrDine.Application.Ordering.Dtos;

namespace QrDine.Application.Auth;

/// <summary>
/// Who may see what. The tenant check has already made sure a token belongs to the restaurant in
/// the address, so these rules only decide between that restaurant's staff and its guests.
/// </summary>
public static class Access
{
    // Clocks and a slow phone: a guest's own order is never older than their pass by more than this.
    private static readonly TimeSpan PassSlack = TimeSpan.FromMinutes(2);

    public static string? TypeOf(ClaimsPrincipal? user) => user?.FindFirst(AppClaims.TokenType)?.Value;
    public static bool IsStaff(ClaimsPrincipal? user) => TypeOf(user) == AppClaims.TypeStaff;
    public static bool IsGuest(ClaimsPrincipal? user) => TypeOf(user) == AppClaims.TypeGuest;

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A token issued for this restaurant (by its address).</summary>
    public static bool IsFor(ClaimsPrincipal? user, string slug) => Same(user?.FindFirst(AppClaims.Slug)?.Value, slug);

    /// <summary>A guest's pass is for this one table.</summary>
    public static bool IsGuestAt(ClaimsPrincipal? user, string? tableCode) =>
        IsGuest(user) && Same(user!.FindFirst(AppClaims.TableCode)?.Value, tableCode);

    /// <summary>A table's running orders: its own guests, or staff.</summary>
    public static bool MayReadTable(ClaimsPrincipal? user, string tableCode) => IsStaff(user) || IsGuestAt(user, tableCode);

    /// <summary>
    /// One order: staff see any; a guest sees their own table's orders that are still running, and
    /// finished ones placed since their pass was issued. So the next people at that table can't page
    /// back through earlier guests' bills.
    /// </summary>
    public static bool MayReadOrder(ClaimsPrincipal? user, OrderDto order)
    {
        if (IsStaff(user)) return true;
        if (!IsGuestAt(user, order.TableCode)) return false;
        if (order.Status is not ("Paid" or "Cancelled")) return true;
        if (!long.TryParse(user!.FindFirst(AppClaims.IssuedAt)?.Value, out var issued)) return false;
        return order.CreatedAt >= DateTimeOffset.FromUnixTimeSeconds(issued).UtcDateTime - PassSlack;
    }

    // ---- the live feed (SignalR) ----

    /// <summary>The restaurant-wide feed (every order, the printers): only that restaurant's staff.</summary>
    public static bool MayJoinStaffFeed(ClaimsPrincipal? user, string slug) => IsStaff(user) && IsFor(user, slug);

    /// <summary>One table's feed: that table's guests, or the restaurant's staff.</summary>
    public static bool MayJoinTableFeed(ClaimsPrincipal? user, string slug, string tableCode) =>
        IsFor(user, slug) && (IsStaff(user) || IsGuestAt(user, tableCode));
}
