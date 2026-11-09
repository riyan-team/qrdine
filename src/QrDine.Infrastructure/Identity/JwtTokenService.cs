using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QrDine.Application.Common;
using QrDine.Domain.Identity;

namespace QrDine.Infrastructure.Identity;

/// <summary>Issues HS256 JWTs for staff and guests.</summary>
public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _opt;
    public JwtTokenService(IOptions<JwtOptions> opt) => _opt = opt.Value;

    public string CreateStaffToken(StaffMember staff, string slug, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, staff.Username),
            new(ClaimTypes.Name, staff.DisplayName),
            new(ClaimTypes.Role, staff.Role.ToString()),
            new(AppClaims.TokenType, AppClaims.TypeStaff),
            new(AppClaims.Slug, slug),
            new(AppClaims.RestaurantId, staff.RestaurantId.ToString()),
            // Checked on every request (StaffSessions): a removed account or a new password ends this token.
            new(AppClaims.StaffId, staff.Id.ToString()),
            new(AppClaims.Stamp, Application.Auth.StaffSessions.StampOf(staff.PasswordHash)),
        };
        foreach (var p in permissions.Distinct())
            claims.Add(new Claim(AppClaims.Permission, p));
        return Build(claims, _opt.StaffHours);
    }

    public string CreateGuestToken(int restaurantId, string slug, string tableCode)
    {
        var claims = new List<Claim>
        {
            new(AppClaims.TokenType, AppClaims.TypeGuest),
            new(AppClaims.Slug, slug),
            new(AppClaims.RestaurantId, restaurantId.ToString()),
            new(AppClaims.TableCode, tableCode),
            // Lets a guest re-open their own finished orders, but not earlier guests' at that table.
            new(AppClaims.IssuedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        return Build(claims, _opt.GuestHours);
    }

    public string CreatePlatformToken(PlatformAdmin admin)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, admin.Username),
            new(ClaimTypes.Name, admin.DisplayName),
            new(AppClaims.TokenType, AppClaims.TypePlatform),
            new(AppClaims.StaffId, admin.Id.ToString()),
            new(AppClaims.Stamp, Application.Auth.StaffSessions.StampOf(admin.PasswordHash)),
        };
        return Build(claims, _opt.StaffHours);
    }

    private string Build(IEnumerable<Claim> claims, int hours)
    {
        // Derive a fixed 32-byte key so HS256 works for ANY configured secret length
        // (a short Jwt:Secret would otherwise throw at signing time). Must match the
        // validation key in AuthSetup.
        var key = new SymmetricSecurityKey(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(_opt.Secret)));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer, audience: _opt.Audience, claims: claims,
            expires: DateTime.UtcNow.AddHours(hours), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
