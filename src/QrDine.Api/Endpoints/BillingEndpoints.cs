using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Billing;

namespace QrDine.Api.Endpoints;

// Billing is a cashier/manager job — gated to CanPay.
public static class BillingEndpoints
{
    public static void MapBilling(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}").RequireAuthorization(Policies.CanPay).AddEndpointFilter<ValidationFilter>();

        // Live bill preview for a table.
        g.MapGet("/tables/{code}/bill", async (string code, BillingService svc)
            => Results.Ok(await svc.GetBill(code)));

        // Settle the table → numbered invoice; closes the session. The settling cashier
        // is passed through so an FBR filing is stamped with their POSID/token.
        g.MapPost("/tables/{code}/settle", async (string code, SettleRequest body, ClaimsPrincipal user, BillingService svc)
            => Results.Ok(await svc.Settle(code, body, Operator(user))));

        // Split-bill: record a partial payment; auto-settles when the bill is covered.
        g.MapPost("/tables/{code}/pay-part", async (string code, PayPartRequest body, ClaimsPrincipal user, BillingService svc)
            => Results.Ok(await svc.PayPartial(code, body, Operator(user))));

        // Reprint a settled invoice.
        g.MapGet("/invoices/{id:int}", async (int id, BillingService svc) =>
        {
            var inv = await svc.GetInvoice(id);
            return inv is null ? Results.NotFound() : Results.Ok(inv);
        });
    }

    private static string? Operator(ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
