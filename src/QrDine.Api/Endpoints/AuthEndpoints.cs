using QrDine.Api.Validation;
using QrDine.Application.Auth;

namespace QrDine.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}").AddEndpointFilter<ValidationFilter>();

        // Staff login → returns a role-carrying JWT.
        g.MapPost("/auth/login", async (LoginRequest body, AuthService svc)
            => Results.Ok(await svc.Login(body)));

        // Guest scans a table → gets a short-lived token scoped to that table.
        g.MapPost("/tables/{code}/session", async (string code, AuthService svc)
            => Results.Ok(await svc.IssueGuestSession(code)));
    }
}
