using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Expenses;
using QrDine.Application.Inventory;
using QrDine.Application.Purchasing;
using QrDine.Application.Reports;

namespace QrDine.Api.Endpoints;

// The back office: supplier khata, expenses, waste and the reports built from them.
// Each group is gated by its own permission, which the owner can grant per role.
public static class BusinessEndpoints
{
    private static string Who(ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    public static void MapBusiness(this IEndpointRouteBuilder app)
    {
        // ---- supplier khata ----
        var suppliers = app.MapGroup("/api/r/{slug}/admin/suppliers")
            .RequireAuthorization(Policies.CanManageSuppliers)
            .RequiresPlan(r => r.FeatureInventory)
            .AddEndpointFilter<ValidationFilter>();

        suppliers.MapGet("", async (SupplierService svc) => Results.Ok(await svc.List()));
        suppliers.MapPost("", async (SupplierInput body, SupplierService svc) => Results.Ok(new { id = await svc.Create(body) }));
        suppliers.MapPut("/{id:int}", async (int id, SupplierInput body, SupplierService svc) => { await svc.Update(id, body); return Results.NoContent(); });
        suppliers.MapDelete("/{id:int}", async (int id, SupplierService svc) => { await svc.Archive(id); return Results.NoContent(); });
        suppliers.MapGet("/{id:int}/ledger", async (int id, SupplierService svc) => Results.Ok(await svc.Ledger(id)));
        suppliers.MapPost("/{id:int}/purchases", async (int id, PurchaseInput body, ClaimsPrincipal user, SupplierService svc) =>
            Results.Ok(new { id = await svc.AddPurchase(id, body, Who(user)) }));
        suppliers.MapPost("/{id:int}/payments", async (int id, SupplierPaymentInput body, ClaimsPrincipal user, SupplierService svc) =>
            Results.Ok(new { id = await svc.AddPayment(id, body, Who(user)) }));
        suppliers.MapPost("/{id:int}/returns", async (int id, SupplierReturnInput body, ClaimsPrincipal user, SupplierService svc) =>
            Results.Ok(new { id = await svc.AddReturn(id, body, Who(user)) }));
        suppliers.MapDelete("/entries/{entryId:int}", async (int entryId, SupplierService svc) => { await svc.DeleteEntry(entryId); return Results.NoContent(); });

        // ---- expenses ----
        var expenses = app.MapGroup("/api/r/{slug}/admin/expenses")
            .RequireAuthorization(Policies.CanManageExpenses)
            .RequiresPlan(r => r.FeatureReports)
            .AddEndpointFilter<ValidationFilter>();

        expenses.MapGet("", async (string? from, string? to, string? category, int? branchId, ExpenseService svc) =>
            Results.Ok(await svc.List(from, to, category, branchId)));
        expenses.MapPost("", async (ExpenseInput body, ClaimsPrincipal user, ExpenseService svc) =>
            Results.Ok(new { id = await svc.Create(body, Who(user)) }));
        expenses.MapPut("/{id:int}", async (int id, ExpenseInput body, ExpenseService svc) => { await svc.Update(id, body); return Results.NoContent(); });
        expenses.MapDelete("/{id:int}", async (int id, ExpenseService svc) => { await svc.Delete(id); return Results.NoContent(); });

        // ---- waste belongs with the rest of stock ----
        app.MapPost("/api/r/{slug}/admin/stock/{id:int}/waste", async (int id, WasteInput body, InventoryService svc) =>
            { await svc.Waste(id, body.Quantity, body.Reason); return Results.NoContent(); })
            .RequireAuthorization(Policies.CanManageInventory)
            .RequiresPlan(r => r.FeatureInventory)
            .AddEndpointFilter<ValidationFilter>();

        // ---- reports ----
        app.MapGet("/api/r/{slug}/admin/reports/business", async (string? from, string? to, int? branchId, BusinessReportService svc) =>
            Results.Ok(await svc.Build(from, to, branchId)))
            .RequireAuthorization(Policies.CanViewReports)
            .RequiresPlan(r => r.FeatureReports);

        // Guests: which dishes to show as "most ordered". Dish ids only.
        app.MapGet("/api/r/{slug}/popular", async (BusinessReportService svc) => Results.Ok(await svc.PopularItems()))
            .RequireRateLimiting("public");
    }
}
