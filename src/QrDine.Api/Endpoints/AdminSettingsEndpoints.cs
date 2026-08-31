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

        // Receipt branding — printed header/footer text + a centred logo.
        g.MapGet("/receipt-settings", async (RestaurantAdminService svc) => Results.Ok(await svc.GetReceiptSettings()));
        g.MapPut("/receipt-settings", async (ReceiptSettingsInput body, RestaurantAdminService svc) => { await svc.SetReceiptSettings(body); return Results.NoContent(); });

        // Logo upload (multipart). Max 2 MB, images only. Sets the restaurant's LogoUrl.
        g.MapPost("/logo", async (IFormFile file, IFileStorage storage, RestaurantAdminService svc) =>
        {
            if (file is null || file.Length == 0) return Results.BadRequest(new { detail = "No file." });
            if (file.Length > 2 * 1024 * 1024) return Results.BadRequest(new { detail = "Logo must be under 2 MB." });
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return Results.BadRequest(new { detail = "Use a PNG, JPG or WebP image." });
            await using var stream = file.OpenReadStream();
            var url = await storage.Save(stream, ext, "logos");
            await svc.SetLogoUrl(url);
            return Results.Ok(new { logoUrl = url });
        }).DisableAntiforgery();

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
