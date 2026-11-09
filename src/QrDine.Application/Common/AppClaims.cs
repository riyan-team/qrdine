namespace QrDine.Application.Common;

/// <summary>Custom JWT claim names, shared by the token issuer and the API guards.</summary>
public static class AppClaims
{
    public const string Slug = "slug";       // the restaurant the token belongs to
    public const string RestaurantId = "rid";
    public const string TokenType = "typ";   // "staff" or "guest"
    public const string TableCode = "table"; // guest tokens only
    public const string Permission = "perm"; // staff tokens: one per granted permission
    public const string StaffId = "stf";     // staff tokens: the staff member's row id
    public const string Stamp = "stm";       // staff and platform tokens: changes when the password changes
    public const string IssuedAt = "iat";    // guest tokens: when the table pass was issued (unix seconds)

    public const string TypeStaff = "staff";
    public const string TypeGuest = "guest";
    public const string TypePlatform = "platform";
}
