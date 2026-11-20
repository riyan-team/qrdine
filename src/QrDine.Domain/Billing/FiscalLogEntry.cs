using QrDine.Domain.Common;

namespace QrDine.Domain.Billing;

/// <summary>
/// One try at sending a bill to the tax office (FBR or PRA): when, how it started (at settle, the automatic
/// resend, a "Send" press, a test bill), who pressed, what was sent, what came back and how it ended.
/// The tax office log shows these. The token is never in here: it travels in a header, not in the bill.
/// Lines stay on the copy that sent them (never synced) and old ones are cleared after <see cref="KeepDays"/>.
/// </summary>
public class FiscalLogEntry : Entity, ITenantOwned
{
    public const int KeepDays = 60;
    public const int MaxRequestChars = 32_000;
    public const int MaxResponseChars = 16_000;

    public int RestaurantId { get; set; }

    /// <summary>The bill; null for a test bill (which is never saved).</summary>
    public int? InvoiceId { get; set; }
    public string BillNumber { get; set; } = "";
    public int BranchId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;

    public string Office { get; set; } = "";        // FBR / PRA
    public string Environment { get; set; } = "";   // Practice / Test / Live
    public string Trigger { get; set; } = "";       // see FiscalTrigger
    public string? By { get; set; }                 // who pressed; null for the automatic resend
    public long? PosId { get; set; }

    public string Outcome { get; set; } = "";       // see FiscalOutcome
    public string? FiscalNumber { get; set; }
    public string? Error { get; set; }

    /// <summary>The tax office's address it went to, or what happened instead ("Practice: nothing sent").</summary>
    public string? Endpoint { get; set; }
    public int? HttpStatus { get; set; }
    public int DurationMs { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }

    /// <summary>The bill's try number (1 for the first).</summary>
    public int Attempt { get; set; }

    public static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max] + " …(cut)";
}

/// <summary>How a try started.</summary>
public static class FiscalTrigger
{
    public const string Settle = "Settle";       // sent when the bill was settled
    public const string Auto = "Auto";           // the automatic resend
    public const string Manual = "Manual";       // "Send now" on one bill
    public const string Picked = "Picked";       // "Send picked"
    public const string All = "All";             // "Send all waiting"
    public const string Test = "Test";           // the test bill in Settings
}

/// <summary>How a try ended.</summary>
public static class FiscalOutcome
{
    public const string Filed = "Filed";
    public const string Refused = "Refused";
    public const string Waiting = "Waiting";     // couldn't reach the office; sent again later
    public const string Error = "Error";         // something broke in the app before an answer came
}
