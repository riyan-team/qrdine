using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Auth;
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

        // A table's running orders: that table's guests (their phone got a pass when they scanned) or staff.
        g.MapGet("/tables/{code}/orders", async (string code, ClaimsPrincipal user, OrderService svc) =>
            Access.MayReadTable(user, code)
                ? Results.Ok(await svc.TableOrders(code))
                : Results.Problem(detail: "This session is for a different table.", statusCode: 403))
            .RequireAuthorization(Policies.GuestOrStaff);

        // One order (the customer's order-details screen, or staff). A guest only sees their own
        // table's, so order numbers can't be paged through. Someone else's order gets exactly the
        // same answer as a missing one, so it gives nothing away.
        g.MapGet("/orders/{id:int}", async (int id, ClaimsPrincipal user, OrderService svc) =>
        {
            var order = await svc.GetOrder(id);
            if (!Access.MayReadOrder(user, order)) throw new DomainException("Order not found.");
            if (user.FindFirst(AppClaims.TokenType)?.Value == AppClaims.TypeGuest)
                order = order with { GuestCanCancel = await svc.GuestMayCancel(order) };
            return Results.Ok(order);
        }).RequireAuthorization(Policies.GuestOrStaff);

        // The cancelling rules, so staff screens only show the cancel buttons that will work.
        g.MapGet("/orders/cancel-rules", async (OrderService svc) => Results.Ok(await svc.CancelRules()))
            .RequireAuthorization(Policies.StaffAny);

        // Cancel order (staff with permission OR guest for their table)
        g.MapPost("/orders/{id:int}/cancel", async (int id, CancelRequest? body, ClaimsPrincipal user, OrderService svc) =>
        {
            var type = user.FindFirst(AppClaims.TokenType)?.Value;
            var order = await svc.GetOrder(id);
            if (type == AppClaims.TypeStaff)
            {
                var who = Access.CancellerOf(user);
                // The cancel right, or the owner's cashier option (the counter cancels at any stage without it).
                var canCancel = user.FindAll(AppClaims.Permission).Any(c => c.Value == Permissions.OrdersCancel)
                    || (who.AtCounter && (await svc.CancelRules()).CounterAnyStage);
                if (!canCancel)
                    return Results.Problem(detail: "You don't have permission to cancel orders.", statusCode: 403);
                // Staff always say why, and the report keeps who did it: a cancelled order is food that wasn't paid for.
                if (string.IsNullOrWhiteSpace(body?.Reason))
                    return Results.Problem(detail: "Say why this order is being cancelled.", statusCode: 400);
                return Results.Ok(await svc.Cancel(id, body.Reason, by: user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    who: who));
            }
            if (type == AppClaims.TypeGuest)
            {
                var tokenTable = user.FindFirst(AppClaims.TableCode)?.Value;
                if (!string.IsNullOrEmpty(tokenTable) && !string.Equals(tokenTable, order.TableCode, StringComparison.OrdinalIgnoreCase))
                    return Results.Problem(detail: "This order is not for your table.", statusCode: 403);

                if (order.Status is "Ready" or "Served" or "Paid" or "Cancelled")
                    return Results.Problem(detail: $"Order cannot be cancelled when it is {order.Status.ToLower()}.", statusCode: 400);

                return Results.Ok(await svc.Cancel(id, body?.Reason ?? "Cancelled by customer", who: Canceller.Guest));
            }
            return Results.Problem(detail: "This session can't cancel orders.", statusCode: 403);
        }).RequireAuthorization(Policies.GuestOrStaff);

        // Reorder (guest or staff)
        g.MapPost("/orders/{id:int}/reorder", async (int id, ClaimsPrincipal user, OrderService svc) =>
        {
            var type = user.FindFirst(AppClaims.TokenType)?.Value;
            if (type == AppClaims.TypeGuest)
            {
                var order = await svc.GetOrder(id);
                if (!Access.MayReadOrder(user, order))
                    return Results.Problem(detail: "This order is not for your table.", statusCode: 403);
            }
            return Results.Ok(await svc.Reorder(id));
        }).RequireAuthorization(Policies.GuestOrStaff);
    }
}
