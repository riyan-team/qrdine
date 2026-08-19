namespace QrDine.Application.Common;

public record PaymentRequest(decimal AmountPkr, string Reference, string Description);

/// <summary>
/// Result of starting a payment.
/// Status "paid"    → completed (mock/dev), proceed.
/// Status "redirect"→ POST Fields to RedirectUrl (JazzCash hosted checkout).
/// Status "unconfigured" → gateway selected but credentials missing.
/// </summary>
public record PaymentResult(string Provider, string Status, string Reference,
    string? RedirectUrl = null, IReadOnlyDictionary<string, string>? Fields = null, string? Message = null);

/// <summary>
/// Starts a payment. Implementations: a Mock gateway for dev, and JazzCash (real
/// hosted-checkout request + secure hash) that goes live once merchant credentials
/// are configured. Easypaisa / Safepay would follow the same shape.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentResult> CreateCheckout(PaymentRequest req, CancellationToken ct = default);
}
