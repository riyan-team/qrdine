using QrDine.Api.Auth;
using QrDine.Application.Feedback;
using QrDine.Application.Loyalty;

namespace QrDine.Api.Endpoints;

// Customer feedback: guests submit from their table page (public); owners read the
// summary (gated to reports). Tenant comes from the /api/r/{slug} route.
public static class FeedbackEndpoints
{
    public static void MapFeedback(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/r/{slug}/feedback", async (FeedbackInput body, FeedbackService svc, LoyaltyService loyalty, ILogger<FeedbackService> log) =>
        {
            await svc.Submit(body);
            // A member's rating can earn the loyalty feedback bonus; that must never lose the rating itself.
            var bonus = 0;
            try { bonus = await loyalty.AwardFeedback(body.MemberToken); }
            catch (Exception ex) { log.LogWarning(ex, "Feedback bonus not added."); }
            return Results.Ok(new { bonus });
        }).RequireRateLimiting("public");

        app.MapGet("/api/r/{slug}/admin/feedback", async (FeedbackService svc)
                => Results.Ok(await svc.GetSummary()))
            .RequireAuthorization(Policies.CanViewReports);
    }
}
