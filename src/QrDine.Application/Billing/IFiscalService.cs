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
    long? PosId, string? Token,
    FiscalAuthority Authority = FiscalAuthority.FBR, FiscalEnvironment Environment = FiscalEnvironment.Test);

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

/// <summary>Environment: where it actually went (Practice when the server forces practice for everyone).
/// Unreachable: the tax office couldn't be reached at all (no internet, office down), as opposed to a refusal.</summary>
public record FiscalResult(string Provider, FiscalStatus Status, string? Number, string? QrPayload, string? Error,
    string? Environment = null, bool Unreachable = false);

/// <summary>What the transport can do on this install (the online server can't reach a PC's FBR software).</summary>
public interface IFiscalCapabilities
{
    /// <summary>Every branch is in practice whatever it says (Fiscal:Simulate on this server).</summary>
    bool ForcedPractice { get; }
    /// <summary>The FBR IMS software on this computer can be used (the restaurant PC edition only).</summary>
    bool LocalAvailable { get; }
}

/// <summary>A server where nothing is configured: practice for everyone, no local FBR software.</summary>
public sealed class DefaultFiscalCapabilities : IFiscalCapabilities
{
    public bool ForcedPractice { get; init; }
    public bool LocalAvailable { get; init; }
}
