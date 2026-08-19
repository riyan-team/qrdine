using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Common;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Domain.Common;

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
            if (type == AppClaims.TypeGuest)
            {
                var tokenTable = user.FindFirst(AppClaims.TableCode)?.Value;
                if (!string.Equals(tokenTable, body.TableCode, StringComparison.OrdinalIgnoreCase))
                    throw new DomainException("This session is for a different table.");
            }
            return Results.Ok(await svc.Place(body));
        }).RequireAuthorization(Policies.GuestOrStaff);

        // Reading a table's own live orders stays public (the customer just scanned it).
        g.MapGet("/tables/{code}/orders", async (string code, OrderService svc)
            => Results.Ok(await svc.TableOrders(code)));
    }
}
