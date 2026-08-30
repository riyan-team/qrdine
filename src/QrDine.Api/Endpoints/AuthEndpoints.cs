using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Auth;

namespace QrDine.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}").AddEndpointFilter<ValidationFilter>();

        // Staff login → returns a role-carrying JWT. Rate-limited per IP.
        g.MapPost("/auth/login", async (LoginRequest body, AuthService svc)
            => Results.Ok(await svc.Login(body)))
            .RequireRateLimiting("login");

        // A signed-in staff member changes their own password.
        g.MapPost("/auth/change-password", async (ChangePasswordRequest body, ClaimsPrincipal user, AuthService svc) =>
        {
            var username = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            await svc.ChangePassword(username, body);
            return Results.NoContent();
        }).RequireAuthorization(Policies.StaffAny);

        // Guest scans a table → gets a short-lived token scoped to that table.
        g.MapPost("/tables/{code}/session", async (string code, AuthService svc)
            => Results.Ok(await svc.IssueGuestSession(code)))
            .RequireRateLimiting("public");
    }
}
