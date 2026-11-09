using QrDine.Domain.Restaurants;

namespace QrDine.Api.Auth;

/// <summary>
/// Keeps the back office to what a restaurant's plan pays for. Orders, tables and bills are left out on
/// purpose: a restaurant that drops a plan in the middle of service can still finish what's on the floor.
/// </summary>
public static class PlanGate
{
    // Set by TenantResolutionMiddleware, which has already read the restaurant for this request.
    public const string RestaurantKey = "dinein.restaurant";

    public static TBuilder RequiresPlan<TBuilder>(this TBuilder builder, Func<Restaurant, bool> has)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.Items[RestaurantKey] is Restaurant r && !has(r))
                return Results.Json(new
                {
                    title = "Not on your plan", status = 403,
                    detail = "This isn't part of your plan. To add it, call or WhatsApp Dine In on 0309 8480389.",
                }, statusCode: StatusCodes.Status403Forbidden);
            return await next(ctx);
        });
}
