using QrDine.Application.Menu;

namespace QrDine.Api.Endpoints;

public static class MenuEndpoints
{
    public static void MapMenu(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}");

        g.MapGet("", async (string slug, MenuService svc) =>
        {
            var menu = await svc.GetMenu(slug);
            return menu is null ? Results.NotFound() : Results.Ok(menu);
        });

        g.MapGet("/tables", async (MenuService svc) => Results.Ok(await svc.GetTables()));
    }
}
