using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Identity;

namespace QrDine.Infrastructure.Persistence;

/// <summary>
/// Production bootstrap. Seeds only what a fresh deployment genuinely needs:
/// the SaaS platform operator (from configured credentials) and the subscription
/// plan catalogue. NO demo restaurant / menu / tables / staff are created — each
/// restaurant is created through public signup (OnboardingService), which also
/// seeds that restaurant's roles &amp; permissions.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher,
        string? platformUsername, string? platformPassword)
    {
        // Platform operator login (the SaaS owner). Taken from config/env
        // (PlatformAdmin__Username / PlatformAdmin__Password) on every start, so the settings
        // file is the one place to change it: edit it, restart, and the new name and password
        // work while every older operator sign-in stops working. No password is hardcoded.
        if (!string.IsNullOrWhiteSpace(platformUsername) && !string.IsNullOrWhiteSpace(platformPassword))
        {
            var name = platformUsername.Trim().ToLowerInvariant();
            var admin = await db.PlatformAdmins.OrderBy(a => a.Id).FirstOrDefaultAsync();
            if (admin is null)
            {
                db.PlatformAdmins.Add(new PlatformAdmin
                {
                    Username = name,
                    DisplayName = "Platform Admin",
                    PasswordHash = hasher.Hash(platformPassword),
                });
                await db.SaveChangesAsync();
            }
            else if (admin.Username != name || !hasher.Verify(platformPassword, admin.PasswordHash))
            {
                admin.Username = name;
                if (!hasher.Verify(platformPassword, admin.PasswordHash)) admin.PasswordHash = hasher.Hash(platformPassword);
                await db.SaveChangesAsync();
            }
        }

        // Subscription plan catalogue — product configuration the operator edits in the
        // console. Required so restaurants have plans to choose at signup.
        if (!await db.Plans.AnyAsync())
        {
            db.Plans.AddRange(
                new Domain.Subscriptions.Plan { Name = "QR Menu", Description = "Digital menu with table QR codes", PricePkr = 1500, SetupFeePkr = 5000, SortOrder = 1, Reports = false, Inventory = false },
                new Domain.Subscriptions.Plan { Name = "Ordering", Description = "Self-ordering + live tracking", PricePkr = 4000, SetupFeePkr = 10000, SortOrder = 2, Ordering = true, Reports = true },
                new Domain.Subscriptions.Plan { Name = "Operations", Description = "Kitchen display, waiter, ordering", PricePkr = 8000, SetupFeePkr = 15000, SortOrder = 3, Ordering = true, Kitchen = true, Waiter = true, Reports = true, Inventory = true },
                new Domain.Subscriptions.Plan { Name = "Full ERP", Description = "Everything: billing, tax, inventory, reports", PricePkr = 12000, SetupFeePkr = 25000, SortOrder = 4, Ordering = true, Kitchen = true, Waiter = true, Billing = true, Inventory = true, Reports = true }
            );
            await db.SaveChangesAsync();
        }

        await BackfillOrderCreatePermissionAsync(db);
    }

    /// <summary>
    /// Grant the (newer) <c>orders.create</c> permission to restaurants that were
    /// seeded before it existed, so waiters/reception can take orders without a manual
    /// settings change. Idempotent and non-destructive: a restaurant that already has
    /// <c>orders.create</c> for any role is left untouched, so an owner who later revokes
    /// it is never overridden. New restaurants get it from Permissions.DefaultsFor and
    /// so are skipped here. Query filters are bypassed — this runs with no tenant in
    /// context and must see every restaurant's grants.
    /// </summary>
    private static async Task BackfillOrderCreatePermissionAsync(AppDbContext db)
    {
        var withAnyPerm = await db.RolePermissions.IgnoreQueryFilters()
            .Select(p => p.RestaurantId).Distinct().ToListAsync();
        var withCreate = await db.RolePermissions.IgnoreQueryFilters()
            .Where(p => p.Permission == Domain.Identity.Permissions.OrdersCreate)
            .Select(p => p.RestaurantId).Distinct().ToListAsync();

        var toBackfill = withAnyPerm.Except(withCreate).ToList();
        if (toBackfill.Count == 0) return;

        foreach (var rid in toBackfill)
            foreach (var role in new[] { StaffRole.Manager, StaffRole.Cashier, StaffRole.Waiter })
                db.RolePermissions.Add(new RolePermission
                {
                    RestaurantId = rid, Role = role,
                    Permission = Domain.Identity.Permissions.OrdersCreate,
                });
        await db.SaveChangesAsync();
    }
}
