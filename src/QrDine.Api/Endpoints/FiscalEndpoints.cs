using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Application.Billing;

namespace QrDine.Api.Endpoints;

/// <summary>
/// FBR fiscal views + manual retry. Listing is for the owner/manager (view reports);
/// filing/retry needs the invoice.sync permission and stamps the invoice with the
/// signed-in operator's POSID/token.
/// </summary>
public static class FiscalEndpoints
{
    public static void MapFiscal(this IEndpointRouteBuilder app)
    {
        var view = app.MapGroup("/api/r/{slug}/admin/fbr").RequireAuthorization(Policies.CanViewReports);
        view.MapGet("/invoices/synced", async (FiscalSyncService svc) => Results.Ok(await svc.List(synced: true)));
        view.MapGet("/invoices/pending", async (FiscalSyncService svc) => Results.Ok(await svc.List(synced: false)));

        // Retry is gated to invoice.sync and passes the operator so cloud filings use
        // the operator's own POSID/token (falling back to the branch defaults).
        var sync = app.MapGroup("/api/r/{slug}/admin/fbr").RequireAuthorization(Policies.CanSyncInvoice);
        sync.MapPost("/invoices/{id:int}/retry", async (int id, ClaimsPrincipal user, FiscalSyncService svc)
            => Results.Ok(await svc.Retry(id, Operator(user), user.IsInRole("Owner"))));
    }

    // The staff username lives in the token's `sub`; JwtBearer may remap it to NameIdentifier.
    private static string? Operator(ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
