using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Admin;
using QrDine.Application.Auth;
using QrDine.Application.Common;

namespace QrDine.Api.Endpoints;

// Owner/manager settings: tables and staff. Gated to CanManage.
public static class AdminSettingsEndpoints
{
    public static void MapAdminSettings(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}/admin")
            .RequireAuthorization(Policies.CanManageSettings)
            .AddEndpointFilter<ValidationFilter>();

        // branches
        g.MapGet("/branches", async (RestaurantAdminService svc) => Results.Ok(await svc.ListBranches()));
        g.MapPost("/branches", async (BranchInput body, RestaurantAdminService svc) => Results.Ok(new { id = await svc.AddBranch(body) }));
        g.MapPut("/branches/{id:int}", async (int id, BranchInput body, RestaurantAdminService svc) => { await svc.UpdateBranch(id, body); return Results.NoContent(); });
        g.MapDelete("/branches/{id:int}", async (int id, RestaurantAdminService svc) => { await svc.DeleteBranch(id); return Results.NoContent(); });

        // tables
        g.MapGet("/tables", async (RestaurantAdminService svc) => Results.Ok(await svc.ListTables()));
        g.MapPost("/tables", async (TableInput body, RestaurantAdminService svc) => Results.Ok(new { id = await svc.AddTable(body) }));
        g.MapPut("/tables/{id:int}", async (int id, TableInput body, RestaurantAdminService svc) => { await svc.UpdateTable(id, body); return Results.NoContent(); });
        g.MapDelete("/tables/{id:int}", async (int id, RestaurantAdminService svc) => { await svc.DeleteTable(id); return Results.NoContent(); });

        // staff — owner-role accounts can only be created/modified/reset/deleted by an
        // owner (a manager holds settings.manage but must not escalate to owner).
        g.MapGet("/staff", async (RestaurantAdminService svc) => Results.Ok(await svc.ListStaff()));
        g.MapPost("/staff", async (StaffInput body, System.Security.Claims.ClaimsPrincipal user, RestaurantAdminService svc) => Results.Ok(new { id = await svc.AddStaff(body, user.IsInRole("Owner")) }));
        g.MapPut("/staff/{id:int}", async (int id, StaffUpdateInput body, System.Security.Claims.ClaimsPrincipal user, RestaurantAdminService svc) => { await svc.UpdateStaff(id, body, user.IsInRole("Owner")); return Results.NoContent(); });
        g.MapPost("/staff/{id:int}/password", async (int id, ResetPasswordInput body, System.Security.Claims.ClaimsPrincipal user, RestaurantAdminService svc) => { await svc.ResetPassword(id, body.Password, user.IsInRole("Owner")); return Results.NoContent(); });
        g.MapDelete("/staff/{id:int}", async (int id, System.Security.Claims.ClaimsPrincipal user, RestaurantAdminService svc) => { await svc.DeleteStaff(id, user.IsInRole("Owner")); return Results.NoContent(); });

        // Service settings (waiter auto-assignment).
        g.MapGet("/service-settings", async (RestaurantAdminService svc) => Results.Ok(await svc.GetServiceSettings()));
        g.MapPut("/service-settings", async (ServiceSettingsInput body, RestaurantAdminService svc) => { await svc.SetServiceSettings(body); return Results.NoContent(); });

        // Billing settings — Tax/GST on/off + rate, discount on/off, receipt on/off, stock
        // tracking on/off. Gated to settings.manage (authorized users only).
        g.MapGet("/billing-settings", async (RestaurantAdminService svc) => Results.Ok(await svc.GetBillingSettings()));
        g.MapPut("/billing-settings", async (BillingSettingsInput body, RestaurantAdminService svc) => { await svc.SetBillingSettings(body); return Results.NoContent(); });

        // Receipt branding — printed header/footer text + a centred logo.
        g.MapGet("/receipt-settings", async (RestaurantAdminService svc) => Results.Ok(await svc.GetReceiptSettings()));
        g.MapPut("/receipt-settings", async (ReceiptSettingsInput body, RestaurantAdminService svc) => { await svc.SetReceiptSettings(body); return Results.NoContent(); });

        // Logo upload (multipart). Max 2 MB; validated by content, not filename.
        g.MapPost("/logo", async (HttpRequest req, IFileStorage storage, RestaurantAdminService svc, CancellationToken ct) =>
        {
            var img = await ImageUpload.ReadAsync(req, 2 * 1024 * 1024, ct);
            using var stream = new MemoryStream(img.Data);
            var url = await storage.Save(stream, img.Extension, "logos", ct);
            await svc.SetLogoUrl(url);
            return Results.Ok(new { logoUrl = url });
        }).DisableAntiforgery();

        // Plan status for the owner's "renew soon" banner.
        g.MapGet("/subscription", async (RestaurantAdminService svc, IConfiguration config, CancellationToken ct) =>
        {
            var access = Middleware.SubscriptionPolicy.For(await svc.GetPaidThrough(ct), config);
            return Results.Ok(new { state = access.State.ToString(), daysLeft = access.DaysLeft });
        });

        // FBR fiscal settings (owner enable + seller NTN/STRN). Branch POS config
        // rides on the branch endpoints above.
        g.MapGet("/fbr", async (RestaurantAdminService svc) => Results.Ok(await svc.GetFbrSettings()));
        g.MapPut("/fbr", async (FbrSettingsInput body, RestaurantAdminService svc) => { await svc.SetFbrSettings(body); return Results.NoContent(); });

        // Permissions matrix — owner only (so a manager can't grant themselves more).
        var owner = app.MapGroup("/api/r/{slug}/admin").RequireAuthorization(Policies.OwnerOnly);
        owner.MapGet("/permissions", async (PermissionService svc) => Results.Ok(await svc.GetMatrix()));
        owner.MapPut("/permissions", async (SetRolePermissionsRequest body, PermissionService svc) => { await svc.SetRole(body.Role, body.Permissions); return Results.NoContent(); });
    }
}
