using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QrDine.Api.Auth;
using QrDine.Application.Loyalty;

namespace QrDine.Api.Endpoints;

// Loyalty points, rewards and review links.
// Guests (the table's QR page) are identified by the member token their phone keeps after joining,
// sent as the X-Member-Token header. Spending points always needs staff approval.
// Owners/managers set the programme up (settings.manage); reception approves rewards and attaches a
// member to a running bill (orders.pay).
public static class LoyaltyEndpoints
{
    private const string TokenHeader = "X-Member-Token";

    public static void MapLoyalty(this IEndpointRouteBuilder app)
    {
        // ---- guest (public, rate-limited like feedback) ----
        var g = app.MapGroup("/api/r/{slug}/loyalty").RequireRateLimiting("public");

        g.MapGet("", async (LoyaltyService svc) => Results.Ok(await svc.GetPublic()));

        g.MapPost("/join", async (LoyaltyJoinInput body, LoyaltyService svc) => Results.Ok(await svc.Join(body)));

        g.MapGet("/me", async ([FromHeader(Name = TokenHeader)] string? token, LoyaltyService svc) =>
        {
            var me = await svc.Me(token);
            return me is null ? Results.NotFound() : Results.Ok(me);
        });

        g.MapPost("/link", async ([FromHeader(Name = TokenHeader)] string? token, LoyaltyLinkInput body, LoyaltyService svc)
            => Results.Ok(new { linked = await svc.LinkTable(token, body.TableCode) }));

        g.MapPost("/redeem", async ([FromHeader(Name = TokenHeader)] string? token, LoyaltyRedeemInput body, LoyaltyService svc)
            => Results.Ok(await svc.Redeem(token, body)));

        g.MapPost("/redemptions/{id:int}/cancel", async (int id, [FromHeader(Name = TokenHeader)] string? token, LoyaltyService svc) =>
        {
            await svc.CancelRedemption(token, id);
            return Results.NoContent();
        });

        // ---- owner / manager: programme, rewards, members ----
        var a = app.MapGroup("/api/r/{slug}/admin/loyalty");

        a.MapGet("/program", async (LoyaltyService svc) => Results.Ok(await svc.GetProgram()))
            .RequireAuthorization(Policies.CanManageSettings);
        a.MapPut("/program", async (LoyaltyProgramInput body, LoyaltyService svc) => Results.Ok(await svc.UpdateProgram(body)))
            .RequireAuthorization(Policies.CanManageSettings);

        a.MapGet("/overview", async (LoyaltyService svc) => Results.Ok(await svc.Overview()))
            .RequireAuthorization(Policies.CanManageSettings);

        a.MapGet("/rewards", async (LoyaltyService svc) => Results.Ok(await svc.Rewards()))
            .RequireAuthorization(Policies.CanManageSettings);
        a.MapPost("/rewards", async (LoyaltyRewardInput body, LoyaltyService svc) => Results.Ok(await svc.SaveReward(null, body)))
            .RequireAuthorization(Policies.CanManageSettings);
        a.MapPut("/rewards/{id:int}", async (int id, LoyaltyRewardInput body, LoyaltyService svc) => Results.Ok(await svc.SaveReward(id, body)))
            .RequireAuthorization(Policies.CanManageSettings);
        a.MapDelete("/rewards/{id:int}", async (int id, LoyaltyService svc) =>
        {
            await svc.DeleteReward(id);
            return Results.NoContent();
        }).RequireAuthorization(Policies.CanManageSettings);

        a.MapGet("/members", async (string? q, int? page, int? pageSize, LoyaltyService svc)
                => Results.Ok(await svc.Members(q, page ?? 1, pageSize ?? 20)))
            .RequireAuthorization(Policies.CanManageSettings);
        a.MapGet("/members/{id:int}", async (int id, LoyaltyService svc) => Results.Ok(await svc.Member(id)))
            .RequireAuthorization(Policies.CanManageSettings);
        a.MapPost("/members/{id:int}/adjust", async (int id, LoyaltyAdjustInput body, ClaimsPrincipal user, LoyaltyService svc)
                => Results.Ok(await svc.Adjust(id, body, By(user))))
            .RequireAuthorization(Policies.CanManageSettings);

        // ---- reception: reward requests and the running bill's member ----
        a.MapGet("/redemptions", async (string? status, LoyaltyService svc) => Results.Ok(await svc.Redemptions(status)))
            .RequireAuthorization(Policies.CanPay);
        a.MapPost("/redemptions/{id:int}/approve", async (int id, ClaimsPrincipal user, LoyaltyService svc)
                => Results.Ok(await svc.Approve(id, By(user))))
            .RequireAuthorization(Policies.CanPay);
        a.MapPost("/redemptions/{id:int}/reject", async (int id, LoyaltyDecisionInput? body, ClaimsPrincipal user, LoyaltyService svc)
                => Results.Ok(await svc.Reject(id, By(user), body?.Note)))
            .RequireAuthorization(Policies.CanPay);

        a.MapGet("/tables/{code}", async (string code, LoyaltyService svc) => Results.Ok(await svc.TableMember(code)))
            .RequireAuthorization(Policies.CanPay);
        a.MapPost("/tables/{code}/member", async (string code, LoyaltyAttachInput body, ClaimsPrincipal user, LoyaltyService svc)
                => Results.Ok(await svc.AttachToTable(code, body, By(user))))
            .RequireAuthorization(Policies.CanPay);
        a.MapDelete("/tables/{code}/member", async (string code, LoyaltyService svc) =>
        {
            await svc.DetachFromTable(code);
            return Results.NoContent();
        }).RequireAuthorization(Policies.CanPay);
    }

    private static string By(ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Name)?.Value ?? "";
}
