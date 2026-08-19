using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Inventory;

namespace QrDine.Api.Endpoints;

// Inventory management — Owner/Manager only.
public static class InventoryEndpoints
{
    public static void MapInventory(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}/admin")
            .RequireAuthorization(Policies.CanManageInventory)
            .AddEndpointFilter<ValidationFilter>();

        g.MapGet("/stock", async (InventoryService svc) => Results.Ok(await svc.ListStock()));
        g.MapPost("/stock", async (StockItemInput body, InventoryService svc) => Results.Ok(new { id = await svc.CreateStockItem(body) }));
        g.MapPut("/stock/{id:int}", async (int id, StockItemInput body, InventoryService svc) => { await svc.UpdateStockItem(id, body); return Results.NoContent(); });
        g.MapDelete("/stock/{id:int}", async (int id, InventoryService svc) => { await svc.DeleteStockItem(id); return Results.NoContent(); });
        g.MapPost("/stock/{id:int}/restock", async (int id, RestockInput body, InventoryService svc) => { await svc.Restock(id, body.Quantity, body.Reason); return Results.NoContent(); });
        g.MapPost("/stock/{id:int}/adjust", async (int id, AdjustInput body, InventoryService svc) => { await svc.Adjust(id, body.NewQuantity, body.Reason); return Results.NoContent(); });

        g.MapGet("/items/{id:int}/recipe", async (int id, InventoryService svc) => Results.Ok(await svc.GetRecipe(id)));
        g.MapPut("/items/{id:int}/recipe", async (int id, RecipeInput body, InventoryService svc) => { await svc.SetRecipe(id, body); return Results.NoContent(); });

        g.MapGet("/movements", async (InventoryService svc) => Results.Ok(await svc.RecentMovements()));
    }
}
