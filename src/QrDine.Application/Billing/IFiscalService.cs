using QrDine.Domain.Billing;
using QrDine.Domain.Restaurants;

namespace QrDine.Application.Billing;

/// <summary>
/// Everything the fiscal transport needs to file one invoice, resolved per-sale
/// from the restaurant (seller identity), the branch (mode/endpoint) and the POS
/// operator (POSID + token). Kept as a value so the transport stays stateless.
/// </summary>
public record FiscalContext(
    string SellerBusinessName, string SellerNtn, string SellerStrn,
    FbrMode Mode, bool Sandbox, string? EndpointOverride,
    long? PosId, string? Token);

/// <summary>
/// Files a settled invoice with FBR and returns its fiscal number + QR payload.
/// Implementations live in Infrastructure: a simulator for demo/testing and the
/// real Local (IMS component) / Cloud (esp/gw.fbr.gov.pk) HTTP transports.
/// The call must be idempotent-safe: the orchestrator never calls it for an
/// invoice already registered.
/// </summary>
public interface IFiscalService
{
    Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default);
}

public record FiscalResult(string Provider, FiscalStatus Status, string? Number, string? QrPayload, string? Error);
