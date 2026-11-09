using QrDine.Api.Auth;
using QrDine.Application.Common;
using QrDine.Domain.Restaurants;

namespace QrDine.Api.Middleware;

/// <summary>
/// Makes each restaurant's changes run one at a time (see <see cref="RestaurantGate"/>).
/// It's a filter rather than middleware because filters run after the request body has been read,
/// so a guest on a slow connection can't hold up the rest of the restaurant.
/// </summary>
public class FloorGateFilter : IEndpointFilter
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(20);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || http.Items[PlanGate.RestaurantKey] is not Restaurant r)
            return await next(context);

        var turn = http.RequestServices.GetRequiredService<FloorTurn>();
        if (!await turn.Take(r.Id, MaxWait, http.RequestAborted))
            return Results.Json(new { title = "Busy", status = 503, detail = "The system is busy for a moment. Please try again." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        try { return await next(context); }
        finally { turn.Finish(); }
    }
}
