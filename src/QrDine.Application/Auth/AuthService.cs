using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;

namespace QrDine.Application.Auth;

/// <summary>Staff login and guest table-session issuance.</summary>
public class AuthService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly ITokenService _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly PermissionService _perms;

    public AuthService(IAppDbContext db, ICurrentTenant tenant, ITokenService tokens, IPasswordHasher hasher, PermissionService perms)
    {
        _db = db; _tenant = tenant; _tokens = tokens; _hasher = hasher; _perms = perms;
    }

    private string Slug => _tenant.Slug ?? throw new DomainException("No restaurant in context.");
    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");

    public async Task<AuthResponse> Login(LoginRequest req, CancellationToken ct = default)
    {
        var user = await _db.Staff.FirstOrDefaultAsync(s => s.Username == req.Username, ct);
        // Same error whether the user is missing or the password is wrong — no account probing.
        if (user is null || !_hasher.Verify(req.Password, user.PasswordHash))
            throw new DomainException("Invalid username or password.");

        var perms = await _perms.GetForRole(user.Role, ct);
        var token = _tokens.CreateStaffToken(user, Slug, perms);
        var features = await LoadFeatures(ct);
        return new AuthResponse(token, user.DisplayName, user.Role.ToString(), perms, features);
    }

    /// <summary>The signed-in restaurant's enabled modules — so the UI hides what
    /// the plan doesn't include (and FBR unless the operator granted it).</summary>
    private async Task<FeaturesDto> LoadFeatures(CancellationToken ct)
    {
        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == TenantId, ct);
        if (r is null) return new FeaturesDto(true, true, true, true, true, true, false);
        return new FeaturesDto(r.FeatureOrdering, r.FeatureKitchen, r.FeatureWaiter,
            r.FeatureBilling, r.FeatureInventory, r.FeatureReports, r.FeatureFbr);
    }

    public async Task<GuestSessionResponse> IssueGuestSession(string tableCode, CancellationToken ct = default)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Code == tableCode, ct)
            ?? throw new DomainException("Unknown table.");
        var token = _tokens.CreateGuestToken(TenantId, Slug, table.Code);
        return new GuestSessionResponse(token, table.Code);
    }
}
