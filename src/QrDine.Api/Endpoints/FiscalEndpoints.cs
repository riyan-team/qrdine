using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Application.Billing;

namespace QrDine.Api.Endpoints;

/// <summary>
/// The tax office (FBR / PRA) screens. Listing and the setup check are for whoever sees reports; sending (one bill,
/// the picked ones, or every waiting one) needs the invoice.sync permission and uses the signed-in cashier's
/// POSID and token, falling back to the branch's. The test bill is for whoever manages settings, and never goes to Live.
/// </summary>
public static class FiscalEndpoints
{
    public static void MapFiscal(this IEndpointRouteBuilder app)
    {
        var view = app.MapGroup("/api/r/{slug}/admin/fbr").RequireAuthorization(Policies.CanViewReports);
        view.MapGet("/invoices/synced", async (FiscalSyncService svc) => Results.Ok(await svc.List(synced: true)));
        view.MapGet("/invoices/pending", async (FiscalSyncService svc) => Results.Ok(await svc.List(synced: false)));
        view.MapGet("/check", async (FiscalSyncService svc) => Results.Ok(await svc.Check()));

        var sync = app.MapGroup("/api/r/{slug}/admin/fbr").RequireAuthorization(Policies.CanSyncInvoice);
        sync.MapPost("/invoices/{id:int}/retry", async (int id, ClaimsPrincipal user, FiscalSyncService svc)
            => Results.Ok(await svc.Retry(id, Operator(user), user.IsInRole("Owner"))));
        // The picked bills, or (no ids) every waiting one, up to 25 a press.
        sync.MapPost("/invoices/retry", async (FiscalRetryManyRequest? body, ClaimsPrincipal user, FiscalSyncService svc)
            => Results.Ok(await svc.RetryMany(body?.Ids, Operator(user), user.IsInRole("Owner"))));

        var settings = app.MapGroup("/api/r/{slug}/admin/fbr").RequireAuthorization(Policies.CanManageSettings);
        settings.MapPost("/branches/{id:int}/test", async (int id, ClaimsPrincipal user, FiscalSyncService svc)
            => Results.Ok(await svc.SendTestInvoice(id, Operator(user))));
    }

    // The staff username lives in the token's `sub`; JwtBearer may remap it to NameIdentifier.
    private static string? Operator(ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
