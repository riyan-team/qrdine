using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Platform;

namespace QrDine.Api.Endpoints;

// The SaaS operator control plane — not tenant-scoped.
public static class PlatformEndpoints
{
    public static void MapPlatform(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/platform/login", async (PlatformLoginRequest body, PlatformService svc)
            => Results.Ok(await svc.Login(body))).AddEndpointFilter<ValidationFilter>()
            .RequireRateLimiting("login");

        // Public: active plans, for the sign-up banners.
        app.MapGet("/api/plans", async (PlatformService svc) => Results.Ok(await svc.ListPlans(activeOnly: true)));

        var g = app.MapGroup("/api/platform").RequireAuthorization(Policies.Platform).AddEndpointFilter<ValidationFilter>();
        g.MapGet("/stats", async (PlatformService svc) => Results.Ok(await svc.Stats()));
        g.MapGet("/restaurants", async (PlatformService svc) => Results.Ok(await svc.ListRestaurants()));
        g.MapPost("/restaurants/{id:int}/plan", async (int id, SetPlanRequest body, PlatformService svc) => { await svc.SetPlan(id, body.PlanId); return Results.NoContent(); });
        g.MapPost("/restaurants/{id:int}/status", async (int id, SetStatusRequest body, PlatformService svc) => { await svc.SetStatus(id, body.Status); return Results.NoContent(); });
        // Subscription billing: record an offline payment (setup fee or N months) + history.
        g.MapPost("/restaurants/{id:int}/payment", async (int id, RecordPaymentRequest body, System.Security.Claims.ClaimsPrincipal user, PlatformService svc)
            => Results.Ok(await svc.RecordPayment(id, body, user.Identity?.Name ?? "operator")));
        g.MapGet("/restaurants/{id:int}/payments", async (int id, PlatformService svc) => Results.Ok(await svc.ListPayments(id)));
        // Permanently delete a restaurant and all its data.
        g.MapDelete("/restaurants/{id:int}", async (int id, PlatformService svc) => { await svc.DeleteRestaurant(id); return Results.NoContent(); });
        // SaaS gate: grant/revoke FBR fiscal integration for a restaurant.
        g.MapPost("/restaurants/{id:int}/fbr", async (int id, SetFbrRequest body, PlatformService svc) => { await svc.SetFbr(id, body.Enabled); return Results.NoContent(); });

        // Plan catalogue management
        g.MapGet("/plans", async (PlatformService svc) => Results.Ok(await svc.ListPlans()));
        g.MapPost("/plans", async (PlanInput body, PlatformService svc) => Results.Ok(new { id = await svc.CreatePlan(body) }));
        g.MapPut("/plans/{id:int}", async (int id, PlanInput body, PlatformService svc) => { await svc.UpdatePlan(id, body); return Results.NoContent(); });
        g.MapDelete("/plans/{id:int}", async (int id, PlatformService svc) => { await svc.DeletePlan(id); return Results.NoContent(); });
    }
}
