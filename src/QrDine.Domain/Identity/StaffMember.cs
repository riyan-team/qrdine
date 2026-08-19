using QrDine.Domain.Common;

namespace QrDine.Domain.Identity;

/// <summary>The roles a staff account can hold. Higher roles inherit lower rights via policy.</summary>
public enum StaffRole
{
    Owner,     // full access + reports
    Manager,   // everything operational
    Cashier,   // reception: approve, take payment
    Kitchen,   // KDS: cook, ready
    Waiter,    // assign, serve
}

/// <summary>A login for a member of a restaurant's staff.</summary>
public class StaffMember : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Username { get; set; } = "";     // unique within a restaurant
    public string DisplayName { get; set; } = "";
    public StaffRole Role { get; set; }
    public string PasswordHash { get; set; } = "";

    // ---- FBR POS operator credentials ----
    // Assigned at user creation. When a cashier syncs an invoice these override the
    // branch defaults, so each terminal/operator files under its own registered POS.
    public long? FbrPosId { get; set; }
    public string? FbrToken { get; set; }          // cloud bearer token for this operator
}
