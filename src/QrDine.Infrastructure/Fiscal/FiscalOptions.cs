namespace QrDine.Infrastructure.Fiscal;

/// <summary>Bound from the "Fiscal" config section.</summary>
public class FiscalOptions
{
    /// <summary>
    /// Forces every branch on this server into Practice (made-up numbers, nothing sent), whatever its own setting.
    /// The default, so a fresh install never files by accident. Set false where restaurants file for real; each
    /// branch then starts in Practice anyway and the owner moves it to Test, then Live.
    /// </summary>
    public bool Simulate { get; set; } = true;

    /// <summary>
    /// The FBR IMS software on this computer may be used ("On this computer" mode). Only the restaurant PC edition
    /// sets this: on the online server, localhost is the server, not the restaurant's counter PC.
    /// </summary>
    public bool AllowLocal { get; set; }

    /// <summary>Every few minutes, bills that couldn't be sent are tried again (0 turns the automatic resend off).</summary>
    public int AutoRetryMinutes { get; set; } = 3;

    /// <summary>FBR IMS component endpoint on the POS PC (Local mode).</summary>
    public string LocalUrl { get; set; } = "http://localhost:8524/api/IMSFiscal/GetInvoiceNumberByModel";

    /// <summary>FBR cloud gateway — sandbox (testing).</summary>
    public string CloudSandboxUrl { get; set; } = "https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData";

    /// <summary>FBR cloud gateway — production (live).</summary>
    public string CloudProductionUrl { get; set; } = "https://gw.fbr.gov.pk/imsp/v1/api/Live/PostData";

    /// <summary>PRA (Punjab) IMS — sandbox (testing).</summary>
    public string PraSandboxUrl { get; set; } = "https://ims.pral.com.pk/ims/sandbox/api/Live/PostData";

    /// <summary>PRA (Punjab) IMS — production (live).</summary>
    public string PraProductionUrl { get; set; } = "https://ims.pral.com.pk/ims/production/api/Live/PostData";

    /// <summary>What a PRA receipt's QR code opens: PRA's own page for that invoice number.</summary>
    public string PraVerifyUrl { get; set; } = "https://reg.pra.punjab.gov.pk/IMSFiscalReport/SearchPOSInvoice_Report.aspx?PRAInvNo={0}";
}
