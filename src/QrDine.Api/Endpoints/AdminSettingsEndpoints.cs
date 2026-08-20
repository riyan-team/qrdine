using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Admin;
using QrDine.Application.Auth;

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

        // staff
        g.MapGet("/staff", async (RestaurantAdminService svc) => Results.Ok(await svc.ListStaff()));
        g.MapPost("/staff", async (StaffInput body, RestaurantAdminService svc) => Results.Ok(new { id = await svc.AddStaff(body) }));
        g.MapPut("/staff/{id:int}", async (int id, StaffUpdateInput body, RestaurantAdminService svc) => { await svc.UpdateStaff(id, body); return Results.NoContent(); });
        g.MapPost("/staff/{id:int}/password", async (int id, ResetPasswordInput body, RestaurantAdminService svc) => { await svc.ResetPassword(id, body.Password); return Results.NoContent(); });
        g.MapDelete("/staff/{id:int}", async (int id, RestaurantAdminService svc) => { await svc.DeleteStaff(id); return Results.NoContent(); });

        // Service settings (waiter auto-assignment).
        g.MapGet("/service-settings", async (RestaurantAdminService svc) => Results.Ok(await svc.GetServiceSettings()));
        g.MapPut("/service-settings", async (ServiceSettingsInput body, RestaurantAdminService svc) => { await svc.SetServiceSettings(body); return Results.NoContent(); });

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
