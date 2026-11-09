using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Identity;

namespace QrDine.Application.Auth;

/// <summary>
/// A staff sign-in lasts 12 hours, so every request checks it against the database instead of
/// trusting the token alone. A removed staff member or a changed password ends the sign-in at once,
/// and a role or permission change takes effect on the next request, not at the next sign-in.
/// </summary>
public class StaffSessions
{
    private readonly IAppDbContext _db;
    public StaffSessions(IAppDbContext db) => _db = db;

    /// <summary>
    /// A short fingerprint of the stored password hash. It goes into the token, so a new password
    /// (which always gets a new hash) makes every older token stop working.
    /// </summary>
    public static string StampOf(string? passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? "")))[..16];

    public sealed record Current(StaffMember Staff, string[] Permissions);

    /// <summary>The staff member behind this token as they are now, or null when the sign-in has ended.</summary>
    public async Task<Current?> Check(ClaimsPrincipal? user, CancellationToken ct = default)
    {
        if (user?.FindFirst(AppClaims.TokenType)?.Value != AppClaims.TypeStaff) return null;
        if (!int.TryParse(user.FindFirst(AppClaims.StaffId)?.Value, out var id)) return null;
        if (!int.TryParse(user.FindFirst(AppClaims.RestaurantId)?.Value, out var restaurantId)) return null;

        var staff = await _db.Staff.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.RestaurantId == restaurantId, ct);
        if (staff is null) return null;
        var stamp = user.FindFirst(AppClaims.Stamp)?.Value ?? "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stamp), Encoding.ASCII.GetBytes(StampOf(staff.PasswordHash))))
            return null;

        var perms = staff.Role == StaffRole.Owner
            ? Permissions.All
            : await _db.RolePermissions.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.RestaurantId == restaurantId && p.Role == staff.Role)
                .Select(p => p.Permission).ToArrayAsync(ct);
        return new Current(staff, perms);
    }

    /// <summary>The same sign-in carrying today's role and permissions instead of the ones it was issued with.</summary>
    public static ClaimsPrincipal WithCurrentAccess(ClaimsPrincipal user, Current now)
    {
        var old = user.Identity as ClaimsIdentity ?? new ClaimsIdentity();
        var roleType = old.RoleClaimType;
        var kept = old.Claims.Where(c => c.Type != roleType && c.Type != ClaimTypes.Role && c.Type != "role" && c.Type != AppClaims.Permission);
        var fresh = new ClaimsIdentity(kept, old.AuthenticationType, old.NameClaimType, roleType);
        fresh.AddClaim(new Claim(roleType, now.Staff.Role.ToString()));
        foreach (var p in now.Permissions.Distinct())
            fresh.AddClaim(new Claim(AppClaims.Permission, p));
        return new ClaimsPrincipal(fresh);
    }
}
