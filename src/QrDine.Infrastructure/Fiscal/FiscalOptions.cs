namespace QrDine.Infrastructure.Fiscal;

/// <summary>Bound from the "Fiscal" config section.</summary>
public class FiscalOptions
{
    /// <summary>
    /// When true (dev/demo default), invoices are fiscalised by a local simulator —
    /// no FBR network call. Set false in production to use the real Local IMS
    /// component / Cloud gateway transport chosen per branch.
    /// </summary>
    public bool Simulate { get; set; } = true;

    /// <summary>FBR IMS component endpoint on the POS PC (Local mode).</summary>
    public string LocalUrl { get; set; } = "http://localhost:8524/api/IMSFiscal/GetInvoiceNumberByModel";

    /// <summary>FBR cloud gateway — sandbox (testing).</summary>
    public string CloudSandboxUrl { get; set; } = "https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData";

    /// <summary>FBR cloud gateway — production (live).</summary>
    public string CloudProductionUrl { get; set; } = "https://gw.fbr.gov.pk/imsp/v1/api/Live/PostData";
}
