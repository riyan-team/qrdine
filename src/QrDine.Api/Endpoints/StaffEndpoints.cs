using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;

namespace QrDine.Api.Endpoints;

// Staff-facing: the live board plus the guarded lifecycle transitions.
// Each transition is gated to the role that performs it in a real restaurant.
public static class StaffEndpoints
{
    public static void MapStaff(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}").AddEndpointFilter<ValidationFilter>();

        g.MapGet("/staff/orders", async (OrderService svc) => Results.Ok(await svc.StaffOrders()))
            .RequireAuthorization(Policies.StaffAny);

        g.MapPost("/orders/{id:int}/approve", async (int id, OrderService svc) => Results.Ok(await svc.Approve(id)))
            .RequireAuthorization(Policies.CanApprove);
        g.MapPost("/orders/{id:int}/cook", async (int id, CookRequest body, OrderService svc) => Results.Ok(await svc.Cook(id, body.EtaMinutes)))
            .RequireAuthorization(Policies.CanCook);
        g.MapPost("/orders/{id:int}/ready", async (int id, OrderService svc) => Results.Ok(await svc.Ready(id)))
            .RequireAuthorization(Policies.CanCook);
        g.MapPost("/orders/{id:int}/assign", async (int id, AssignRequest body, OrderService svc) => Results.Ok(await svc.Assign(id, body.WaiterName)))
            .RequireAuthorization(Policies.CanServe);
        g.MapPost("/orders/{id:int}/serve", async (int id, OrderService svc) => Results.Ok(await svc.Serve(id)))
            .RequireAuthorization(Policies.CanServe);
        g.MapPost("/orders/{id:int}/pay", async (int id, OrderService svc) => Results.Ok(await svc.Pay(id)))
            .RequireAuthorization(Policies.CanPay);
        g.MapPost("/orders/{id:int}/cancel", async (int id, CancelRequest body, OrderService svc) => Results.Ok(await svc.Cancel(id, body.Reason)))
            .RequireAuthorization(Policies.CanCancel);
    }
}
