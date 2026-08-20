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
            .AddEndpointFilter<ValidationFilter>();

        // Public: resolve a typed restaurant name OR address (slug) to its slug so
        // staff can sign in with the name they know. Exact, case-insensitive match
        // (no partial search) to avoid enumerating the tenant list.
        app.MapGet("/api/restaurants/lookup", async (string? q, IAppDbContext db, CancellationToken ct) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length == 0) return Results.NotFound();
            var lower = query.ToLowerInvariant();
            var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Slug == lower, ct)
                 ?? await db.Restaurants.FirstOrDefaultAsync(x => x.Name.ToLower() == lower, ct);
            return r is null ? Results.NotFound() : Results.Ok(new { slug = r.Slug, name = r.Name });
        });
    }
}
