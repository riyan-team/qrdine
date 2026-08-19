using QrDine.Application.Common;

namespace QrDine.Api.Endpoints;

// Starts a payment (used by sign-up to buy a plan). Public — no tenant yet.
public static class PaymentEndpoints
{
    public static void MapPayments(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/checkout", async (PaymentRequest body, IPaymentGateway gateway)
            => Results.Ok(await gateway.CreateCheckout(body)));
    }
}
