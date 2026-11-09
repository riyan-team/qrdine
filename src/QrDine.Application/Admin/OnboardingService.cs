using Microsoft.EntityFrameworkCore;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Menu;
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
        // Sign-in names are unique across every restaurant, so one box is enough to log in.
        var username = LoginName.Normalize(req.OwnerUsername);
        if (await _db.Staff.IgnoreQueryFilters().AnyAsync(s => s.Username == username, ct))
            throw new DomainException("This mobile number already has an account. Please sign in instead.");

        // Nobody chooses an address any more: it comes from the restaurant's name.
        var slug = string.IsNullOrWhiteSpace(req.Slug)
            ? await UniqueSlug(req.RestaurantName, ct)
            : req.Slug.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(req.Slug) && await _db.Restaurants.AnyAsync(r => r.Slug == slug, ct))
            throw new DomainException($"The address '{slug}' is already taken — pick another.");

        // Start every restaurant on a 14-day trial so the operator has a window to
        // collect the setup fee + first month before deciding to lock.
        var restaurant = new Restaurant { Slug = slug, Name = req.RestaurantName.Trim(), Currency = "Rs", PlanExpiresAt = DateTime.UtcNow.AddDays(14) };
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
            Username = username,
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

        await SeedStarterMenu(restaurant.Id, ct);

        var token = _tokens.CreateStaffToken(owner, slug, Permissions.All);
        return BuildResponse(restaurant, owner, token);
    }

    /// <summary>"Karachi Biryani House" → "karachi-biryani-house", with a number added if that's taken.</summary>
    private async Task<string> UniqueSlug(string name, CancellationToken ct)
    {
        var chars = new List<char>();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') chars.Add(c);
            else if (chars.Count > 0 && chars[^1] != '-') chars.Add('-');
        }
        var baseSlug = new string(chars.ToArray()).Trim('-');
        if (baseSlug.Length > 50) baseSlug = baseSlug[..50].Trim('-');
        if (baseSlug.Length < 3) baseSlug = "restaurant";

        var slug = baseSlug;
        for (var n = 2; await _db.Restaurants.AnyAsync(r => r.Slug == slug, ct); n++)
            slug = $"{baseSlug}-{n}";
        return slug;
    }

    /// <summary>Seed the ready-to-edit starter menu (categories + items + Half/Full
    /// variants). RestaurantId is set explicitly — there's no tenant in context yet.</summary>
    private async Task SeedStarterMenu(int restaurantId, CancellationToken ct)
    {
        var cats = StarterMenu.Categories
            .Select((c, i) => new MenuCategory { RestaurantId = restaurantId, Name = c.Name, SortOrder = i + 1 })
            .ToList();
        _db.Categories.AddRange(cats);
        await _db.SaveChangesAsync(ct);   // materialise category ids

        for (int i = 0; i < cats.Count; i++)
        {
            foreach (var it in StarterMenu.Categories[i].Items)
            {
                var mi = new MenuItem
                {
                    RestaurantId = restaurantId, CategoryId = cats[i].Id,
                    Name = it.Name, NameUrdu = it.Urdu, Price = it.Price,
                    Emoji = it.Emoji, PrepMinutes = it.Prep, Available = true,
                };
                if (it.Variants is not null)
                    for (int v = 0; v < it.Variants.Length; v++)
                        mi.Variants.Add(new MenuItemVariant { Name = it.Variants[v].Name, Price = it.Variants[v].Price, SortOrder = v + 1 });
                _db.MenuItems.Add(mi);
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private AuthResponse BuildResponse(Restaurant restaurant, StaffMember owner, string token)
    {
        var features = new FeaturesDto(restaurant.FeatureOrdering, restaurant.FeatureKitchen,
            restaurant.FeatureWaiter, restaurant.FeatureBilling, restaurant.FeatureInventory,
            restaurant.FeatureReports, restaurant.FeatureFbr);
        return new AuthResponse(token, owner.DisplayName, owner.Role.ToString(), Permissions.All, features, restaurant.Name, restaurant.Slug);
    }
}
