using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QrDine.Api.Auth;
using QrDine.Api.Middleware;
using QrDine.Application.Common;
using QrDine.Application.Sync;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Api.Endpoints;

/// <summary>
/// Sync with a restaurant's own PC (phase 1: the server side only).
/// <list type="bullet">
/// <item>The owner pairs a device: <c>POST /api/r/{slug}/admin/sync/devices</c> gives its key once.</item>
/// <item>The device sends that key as <c>X-Device-Key</c> on <c>/api/sync/hello</c>, <c>/snapshot</c>,
/// <c>/pull</c> and <c>/push</c>, and only ever reaches its own restaurant's rows.</item>
/// </list>
/// All of it answers 404 unless <c>Sync:Enabled=true</c>.
/// </summary>
public static class SyncEndpoints
{
    public const string DeviceKeyHeader = "X-Device-Key";
    private const string DeviceItem = "sync.device";
    private const string RestaurantItem = "sync.restaurant";
    private const string AdminPrefix = "/api/r/{slug}/admin/sync";
    private const string DevicePrefix = "/api/sync";

    private static string Who(ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    public static void MapSync(this IEndpointRouteBuilder app)
    {
        // Switched off (the default): the sync routes aren't mapped at all, so nothing of sync runs,
        // not even reading the request body or the sign-in, and every sync address answers 404 to
        // everyone. The switch is read once at start; changing it needs a restart.
        if (!app.ServiceProvider.GetRequiredService<SyncSettings>().Enabled)
        {
            app.Map(AdminPrefix + "/{**rest}", () => Results.NotFound());
            app.Map(DevicePrefix + "/{**rest}", () => Results.NotFound());
            return;
        }

        var admin = app.MapGroup(AdminPrefix)
            .RequireAuthorization(Policies.OwnerOnly);
        admin.MapGet("/devices", async (SyncService svc, CancellationToken ct) => Results.Ok(await svc.Devices(ct)));
        admin.MapPost("/devices", async (PairDeviceInput body, SyncService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(await svc.Pair(body.Name, Who(user), ct)));
        admin.MapDelete("/devices/{id:int}", async (int id, SyncService svc, CancellationToken ct) =>
        {
            await svc.Revoke(id, ct);
            return Results.NoContent();
        });

        var device = app.MapGroup(DevicePrefix)
            .AddEndpointFilter<DeviceFilter>();
        device.MapGet("/hello", async (HttpContext ctx, SyncService svc, CancellationToken ct) =>
            Results.Ok(await svc.Hello(Device(ctx), (Restaurant)ctx.Items[RestaurantItem]!, ct)));
        device.MapGet("/snapshot", async (string? type, int? after, int? limit, HttpContext ctx, SyncService svc, CancellationToken ct) =>
            Results.Ok(await svc.Snapshot(Device(ctx), type, after ?? 0, limit ?? 200, ct)));
        device.MapGet("/pull", async (long? since, int? limit, HttpContext ctx, SyncService svc, CancellationToken ct) =>
            Results.Ok(await svc.Pull(Device(ctx), since ?? 0, limit ?? 200, ct)));
        device.MapPost("/push", async (SyncPushInput body, HttpContext ctx, SyncService svc, CancellationToken ct) =>
        {
            var result = await svc.Push(Device(ctx), body.Changes, ct);
            return result.Busy
                ? Results.Json(new { title = "Busy", status = 409, detail = "The same changes were being saved at that moment. Send the batch again." },
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(result);
        });
    }

    private static SyncDevice Device(HttpContext ctx) => (SyncDevice)ctx.Items[DeviceItem]!;

    /// <summary>
    /// Signs the device in by its key and holds the request to the device's restaurant: the tenant
    /// filter is set from the device, never from the request. A suspended or unpaid restaurant's
    /// device is refused the same way its staff screens are.
    /// </summary>
    private sealed class DeviceFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var svc = http.RequestServices.GetRequiredService<SyncService>();
            var device = await svc.Authenticate(http.Request.Headers[DeviceKeyHeader].ToString(), http.RequestAborted);
            if (device is null)
                return Results.Json(new { title = "Unknown device", status = 401, detail = "This device isn't paired, or it was removed. Pair it again from Settings." },
                    statusCode: StatusCodes.Status401Unauthorized);

            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var r = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == device.RestaurantId, http.RequestAborted);
            if (r is null)
                return Results.Json(new { title = "Unknown device", status = 401 }, statusCode: StatusCodes.Status401Unauthorized);
            var config = http.RequestServices.GetRequiredService<IConfiguration>();
            if (RestaurantAccess.Blocked(r, config) is { } blocked)
                return Results.Json(blocked, statusCode: StatusCodes.Status403Forbidden);

            http.RequestServices.GetRequiredService<CurrentTenant>().Set(r.Id, r.Slug);
            http.Items[DeviceItem] = device;
            http.Items[RestaurantItem] = r;
            await svc.Seen(device, http.RequestAborted);
            return await next(context);
        }
    }
}
