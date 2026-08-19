using QrDine.Api.Validation;
using QrDine.Application.Admin;

namespace QrDine.Api.Endpoints;

// Public sign-up — creates a new restaurant + owner. Not tenant-scoped.
public static class OnboardingEndpoints
{
    public static void MapOnboarding(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/onboard", async (OnboardRequest body, OnboardingService svc)
                => Results.Ok(await svc.Onboard(body)))
            .AddEndpointFilter<ValidationFilter>();
    }
}
