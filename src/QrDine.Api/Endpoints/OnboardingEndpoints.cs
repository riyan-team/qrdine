using Microsoft.EntityFrameworkCore;
using QrDine.Api.Validation;
using QrDine.Application.Admin;
using QrDine.Application.Common;

namespace QrDine.Api.Endpoints;

// Public sign-up — creates a new restaurant + owner. Not tenant-scoped.
public static class OnboardingEndpoints
{
    public static void MapOnboarding(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/onboard", async (OnboardRequest body, OnboardingService svc)
                => Results.Ok(await svc.Onboard(body)))
            .AddEndpointFilter<ValidationFilter>()
            .RequireRateLimiting("register");

        // Public: resolve a typed restaurant name OR address (slug) to its slug so
        // staff can sign in with the name they know. Exact, case-insensitive match
        // (no partial search) to avoid enumerating the tenant list.
        app.MapGet("/api/restaurants/lookup", async (string? q, IAppDbContext db, CancellationToken ct) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length == 0) return Results.NotFound();
            var lower = query.ToLowerInvariant();
            // Normalised form: lowercase, letters+digits only. Because a restaurant's
            // slug defaults to its name with punctuation/spaces stripped, this single
            // form matches whether the owner types the name ("Karachi Biryani House")
            // or the address ("karachi-biryani" / "karachibiryani"), with loose spacing
            // or casing — but it is still an exact match (no partial search), so the
            // tenant list can't be enumerated.
            var norm = new string(lower.Where(char.IsLetterOrDigit).ToArray());
            var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Slug == lower, ct)
                 ?? await db.Restaurants.FirstOrDefaultAsync(x => x.Name.ToLower() == lower, ct)
                 ?? (norm.Length >= 3
                        ? await db.Restaurants.FirstOrDefaultAsync(x => x.Slug == norm, ct)
                        : null);
            return r is null ? Results.NotFound() : Results.Ok(new { slug = r.Slug, name = r.Name });
        });
    }
}
