using QrDine.Api.Auth;
using QrDine.Application.Feedback;

namespace QrDine.Api.Endpoints;

// Customer feedback: guests submit from their table page (public); owners read the
// summary (gated to reports). Tenant comes from the /api/r/{slug} route.
public static class FeedbackEndpoints
{
    public static void MapFeedback(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/r/{slug}/feedback", async (FeedbackInput body, FeedbackService svc) =>
        {
            await svc.Submit(body);
            return Results.NoContent();
        });

        app.MapGet("/api/r/{slug}/admin/feedback", async (FeedbackService svc)
                => Results.Ok(await svc.GetSummary()))
            .RequireAuthorization(Policies.CanViewReports);
    }
}
