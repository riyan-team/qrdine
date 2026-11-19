using QrDine.Domain.Common;

namespace QrDine.Domain.Restaurants;

/// <summary>How this branch's POS talks to the FBR fiscalization service.</summary>
public enum FbrMode
{
    Local,   // FBR IMS software component installed on the POS PC (http://localhost:8524)
    Cloud,   // FBR cloud gateway (esp/gw.fbr.gov.pk) with a bearer token + registered static IP
}

/// <summary>
/// Which tax office this branch files its bills with. A restaurant's sales tax on services goes to the province it
/// is in: Punjab files with PRA; Islamabad (and FBR-registered sellers elsewhere) with FBR.
/// </summary>
public enum FiscalAuthority
{
    FBR,
    PRA,
}

/// <summary>Where this branch's bills go.</summary>
public enum FiscalEnvironment
{
    Practice,   // nothing leaves the app: a made-up number, to learn the screens (receipts say PRACTICE)
    Test,       // the tax office's sandbox, with the test POSID and token it gave you (receipts say TEST)
    Live,       // the real tax office: every bill is a legal tax invoice
}

/// <summary>
/// A physical location of a restaurant. Tables belong to a branch, and each order
/// / session / invoice is stamped with its branch so a chain can report per site.
/// Menu, staff and inventory stay shared at the restaurant level.
///
/// FBR registers a POS per branch, so each branch carries its own fiscal connection
/// settings (mode, sandbox/production, and a default POSID/token that individual
/// cashier logins can override).
/// </summary>
public class Branch : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "Main Branch";
    public string Address { get; set; } = "";
    public bool IsActive { get; set; } = true;

    // ---- Tax office POS registration (per branch) ----
    public FiscalAuthority FiscalAuthority { get; set; } = FiscalAuthority.FBR;
    public FiscalEnvironment FiscalEnvironment { get; set; } = FiscalEnvironment.Practice;
    public FbrMode FbrMode { get; set; } = FbrMode.Cloud;
    public bool FbrSandbox { get; set; } = true;              // kept in step with FiscalEnvironment (true unless Live) for older screens
    public long? FbrPosId { get; set; }                       // branch default POS registration number
    public string? FbrToken { get; set; }                     // branch default cloud bearer token
    public string? FbrEndpointOverride { get; set; }          // optional: override the default URL
    public string FbrStaticIp { get; set; } = "";             // informational: IP registered with FBR (cloud)
}
