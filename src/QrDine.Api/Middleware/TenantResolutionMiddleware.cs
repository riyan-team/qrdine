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

    public async Task Invoke(HttpContext ctx, CurrentTenant tenant, AppDbContext db, IConfiguration config)
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
            ctx.Items[Auth.PlanGate.RestaurantKey] = r;

            // Suspended, or unpaid past the grace days: switched off until the operator acts in
            // the platform console (which lives outside /api/r/, so it is not blocked here).
            var blocked = RestaurantAccess.Blocked(r, config);
            if (blocked is not null)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(blocked);
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

/// <summary>
/// The paid-date rules with this server's settings applied: Subscription:GraceDays
/// (default 3) and Subscription:Enforce (default true; false never warns or locks).
/// </summary>
public static class SubscriptionPolicy
{
    public static Domain.Restaurants.SubscriptionAccess For(DateTime? paidThrough, IConfiguration config)
    {
        var enforce = config.GetValue("Subscription:Enforce", true);
        var grace = config.GetValue("Subscription:GraceDays", Domain.Restaurants.SubscriptionAccess.DefaultGraceDays);
        return Domain.Restaurants.SubscriptionAccess.Evaluate(enforce ? paidThrough : null, DateTime.UtcNow, grace);
    }
}

/// <summary>Whether a restaurant is switched off, and what to tell the person who hits that.</summary>
public static class RestaurantAccess
{
    private const string Help = "Please call or WhatsApp Dine In on 0309 8480389.";

    public static object? Blocked(Domain.Restaurants.Restaurant r, IConfiguration config)
    {
        if (r.Status == Domain.Restaurants.RestaurantStatus.Suspended)
            return new
            {
                title = "This restaurant is suspended", status = 403, suspended = true,
                detail = $"This restaurant's account is suspended. {Help}",
            };
        if (SubscriptionPolicy.For(r.PlanExpiresAt, config).State == Domain.Restaurants.SubscriptionState.Locked)
            return new
            {
                title = "This restaurant's subscription has ended", status = 403, expired = true,
                detail = $"This restaurant's plan has ended, so it is paused until it is renewed. {Help}",
            };
        return null;
    }
}
