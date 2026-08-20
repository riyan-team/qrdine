using Microsoft.EntityFrameworkCore;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Tables;

namespace QrDine.Application.Admin;

/// <summary>
/// Public sign-up: creates a new restaurant (tenant), its first owner login, and
/// a handful of starter tables — then returns an owner token so the UI can log
/// them straight in. This is the only place a tenant is created without one
/// already in context, so RestaurantId is set explicitly.
/// </summary>
public class OnboardingService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;

    public OnboardingService(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens)
    {
        _db = db; _hasher = hasher; _tokens = tokens;
    }

    public async Task<AuthResponse> Onboard(OnboardRequest req, CancellationToken ct = default)
    {
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (await _db.Restaurants.AnyAsync(r => r.Slug == slug, ct))
            throw new DomainException($"The address '{slug}' is already taken — pick another.");

        var restaurant = new Restaurant { Slug = slug, Name = req.RestaurantName.Trim(), Currency = "Rs" };
        // Apply the chosen plan (defaults to the cheapest active plan if none picked).
        // Default to the cheapest active plan. Order on the client — SQLite can't
        // ORDER BY a decimal column.
        var plan = req.PlanId is int pid
            ? await _db.Plans.FirstOrDefaultAsync(p => p.Id == pid, ct)
            : (await _db.Plans.Where(p => p.IsActive).ToListAsync(ct)).OrderBy(p => p.PricePkr).FirstOrDefault();
        if (plan is not null) restaurant.AssignPlan(plan);
        _db.Restaurants.Add(restaurant);
        await _db.SaveChangesAsync(ct);   // materialise the restaurant id

        var owner = new StaffMember
        {
            RestaurantId = restaurant.Id,
            Username = req.OwnerUsername.Trim().ToLowerInvariant(),
            DisplayName = req.OwnerDisplayName.Trim(),
            Role = StaffRole.Owner,
            PasswordHash = _hasher.Hash(req.OwnerPassword),
        };
        _db.Staff.Add(owner);

        var branch = new Branch { RestaurantId = restaurant.Id, Name = "Main Branch" };
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(ct);   // materialise branch id
        for (int i = 1; i <= 4; i++)
            _db.Tables.Add(new DiningTable { RestaurantId = restaurant.Id, BranchId = branch.Id, Code = $"T{i}", Name = $"Table {i}" });

        // Seed default role permissions (owner is always full, not stored).
        foreach (var role in new[] { StaffRole.Manager, StaffRole.Cashier, StaffRole.Kitchen, StaffRole.Waiter })
            foreach (var perm in Permissions.DefaultsFor(role))
                _db.RolePermissions.Add(new RolePermission { RestaurantId = restaurant.Id, Role = role, Permission = perm });

        await _db.SaveChangesAsync(ct);

        var token = _tokens.CreateStaffToken(owner, slug, Permissions.All);
        var features = new FeaturesDto(restaurant.FeatureOrdering, restaurant.FeatureKitchen,
            restaurant.FeatureWaiter, restaurant.FeatureBilling, restaurant.FeatureInventory,
            restaurant.FeatureReports, restaurant.FeatureFbr);
        return new AuthResponse(token, owner.DisplayName, owner.Role.ToString(), Permissions.All, features);
    }
}
