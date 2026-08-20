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
        // Platform operator login (the SaaS owner). Seeded only when credentials are
        // supplied via config/env (PlatformAdmin__Username / PlatformAdmin__Password).
        // In production, set these as environment variables — no password is hardcoded.
        if (!await db.PlatformAdmins.AnyAsync()
            && !string.IsNullOrWhiteSpace(platformUsername)
            && !string.IsNullOrWhiteSpace(platformPassword))
        {
            db.PlatformAdmins.Add(new PlatformAdmin
            {
                Username = platformUsername.Trim(),
                DisplayName = "Platform Admin",
                PasswordHash = hasher.Hash(platformPassword),
            });
            await db.SaveChangesAsync();
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
    }
}
