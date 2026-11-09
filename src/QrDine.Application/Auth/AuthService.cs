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
        var r = await _db.Restaurants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == TenantId, ct);
        var features = LoadFeatures(r);
        return new AuthResponse(token, user.DisplayName, user.Role.ToString(), perms, features, r?.Name ?? "", Slug);
    }

    /// <summary>
    /// Finds who is signing in from their mobile number or username alone, across every
    /// restaurant. Sign-in names are unique, but older data might share one, so the password
    /// decides. Same error for an unknown name and a wrong password, and the same work too.
    /// </summary>
    public async Task<StaffLogin> FindLogin(LoginRequest req, CancellationToken ct = default)
    {
        var key = LoginName.Normalize(req.Username);
        var candidates = await _db.Staff.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Username == key).ToListAsync(ct);
        if (candidates.Count == 0) _hasher.Hash(req.Password);

        var user = candidates.FirstOrDefault(s => _hasher.Verify(req.Password, s.PasswordHash))
            ?? throw new DomainException("That mobile number or password doesn't look right. Please try again.");
        var restaurant = await _db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == user.RestaurantId, ct);
        return new StaffLogin(user, restaurant);
    }

    /// <summary>Issues the session for a login that <see cref="FindLogin"/> accepted.</summary>
    public async Task<AuthResponse> CompleteLogin(StaffLogin login, CancellationToken ct = default)
    {
        var (user, restaurant) = login;
        string[] perms = user.Role == Domain.Identity.StaffRole.Owner
            ? Domain.Identity.Permissions.All
            : await _db.RolePermissions.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.RestaurantId == user.RestaurantId && p.Role == user.Role)
                .Select(p => p.Permission).ToArrayAsync(ct);
        var token = _tokens.CreateStaffToken(user, restaurant.Slug, perms);
        return new AuthResponse(token, user.DisplayName, user.Role.ToString(), perms,
            LoadFeatures(restaurant), restaurant.Name, restaurant.Slug);
    }

    /// <summary>The signed-in restaurant's enabled modules — so the UI hides what
    /// the plan doesn't include (and FBR unless the operator granted it).</summary>
    public static FeaturesDto LoadFeatures(Domain.Restaurants.Restaurant? r)
    {
        if (r is null) return new FeaturesDto(true, true, true, true, true, true, false);
        return new FeaturesDto(r.FeatureOrdering, r.FeatureKitchen, r.FeatureWaiter,
            r.FeatureBilling, r.FeatureInventory, r.FeatureReports, r.FeatureFbr);
    }

    /// <summary>A signed-in staff member changes their own password (verifies the
    /// current one first). Scoped to the tenant by the global query filter.</summary>
    public async Task ChangePassword(string username, ChangePasswordRequest req, CancellationToken ct = default)
    {
        var user = await _db.Staff.FirstOrDefaultAsync(s => s.Username == username, ct)
            ?? throw new DomainException("You are not signed in.");
        if (!_hasher.Verify(req.CurrentPassword, user.PasswordHash))
            throw new DomainException("Your current password is incorrect.");
        if ((req.NewPassword ?? "").Length < 6)
            throw new DomainException("New password must be at least 6 characters.");
        user.PasswordHash = _hasher.Hash(req.NewPassword);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<GuestSessionResponse> IssueGuestSession(string tableCode, CancellationToken ct = default)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Code == tableCode && t.Kind == Domain.Ordering.OrderChannel.Dine, ct)
            ?? throw new DomainException("Unknown table.");
        var token = _tokens.CreateGuestToken(TenantId, Slug, table.Code);
        return new GuestSessionResponse(token, table.Code);
    }
}
