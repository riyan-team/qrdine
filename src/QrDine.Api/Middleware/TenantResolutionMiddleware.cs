using QrDine.Application.Common;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace QrDine.Api.Middleware;

/// <summary>
/// Resolves the tenant for every /api/r/{slug}/... request and sets it on the
/// scoped CurrentTenant, so the DbContext's global query filter auto-scopes all
/// data. In production this would prefer a subdomain or a JWT claim; the slug in
/// the path is the simplest form and keeps guest URLs shareable.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, CurrentTenant tenant, AppDbContext db)
    {
        var slug = ExtractSlug(ctx.Request.Path);
        if (slug is not null)
        {
            // Restaurants is not tenant-filtered, so this lookup is safe.
            var r = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug);
            if (r is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsJsonAsync(new { title = "Unknown restaurant", status = 404 });
                return;
            }
            tenant.Set(r.Id, r.Slug);

            // A suspended restaurant is switched off until the operator reactivates it.
            if (r.Status == Domain.Restaurants.RestaurantStatus.Suspended)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { title = "This restaurant is suspended", status = 403, suspended = true });
                return;
            }

            // A token issued for one restaurant must not be used on another's routes.
            if (ctx.User.Identity?.IsAuthenticated == true)
            {
                var tokenSlug = ctx.User.FindFirst(AppClaims.Slug)?.Value;
                if (tokenSlug is not null && !string.Equals(tokenSlug, slug, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { title = "Token does not belong to this restaurant", status = 403 });
                    return;
                }
            }
        }
        await _next(ctx);
    }

    // Matches /api/r/{slug}/...  → returns {slug}
    private static string? ExtractSlug(PathString path)
    {
        var segs = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs is { Length: >= 3 } && segs[0] == "api" && segs[1] == "r")
            return segs[2];
        return null;
    }
}
