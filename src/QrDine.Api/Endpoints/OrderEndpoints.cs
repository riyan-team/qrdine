using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Common;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Ordering;

namespace QrDine.Api.Endpoints;

// Guest-facing ordering: place an order, and read this table's live orders.
public static class OrderEndpoints
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}").AddEndpointFilter<ValidationFilter>();

        g.MapPost("/orders", async (PlaceOrderRequest body, ClaimsPrincipal user, OrderService svc) =>
        {
            // A guest token may only order for the table it was issued to. Staff
            // (e.g. a waiter ordering on behalf) may order for any table.
            var type = user.FindFirst(AppClaims.TokenType)?.Value;
            if (type == AppClaims.TypeStaff)
            {
                // Taking an order on a table's behalf is a gated action (configurable per role).
                var canCreate = user.FindAll(AppClaims.Permission).Any(c => c.Value == Permissions.OrdersCreate);
                if (!canCreate)
                    return Results.Problem(detail: "You don't have permission to take orders.", statusCode: 403);
                var waiter = user.FindFirst(ClaimTypes.Name)?.Value;
                return Results.Ok(await svc.Place(body, OrderSource.Waiter, waiter));
            }
            if (type != AppClaims.TypeGuest)
                // Not a staff or guest token (e.g. a platform token) — has no business ordering.
                return Results.Problem(detail: "This session can't place orders.", statusCode: 403);
            var tokenTable = user.FindFirst(AppClaims.TableCode)?.Value;
            if (!string.Equals(tokenTable, body.TableCode, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("This session is for a different table.");
            return Results.Ok(await svc.Place(body, OrderSource.Qr));
        }).RequireAuthorization(Policies.GuestOrStaff).RequireRateLimiting("orders");

        // Reading a table's own live orders stays public (the customer just scanned it).
        g.MapGet("/tables/{code}/orders", async (string code, OrderService svc)
            => Results.Ok(await svc.TableOrders(code)));
    }
}
