using QrDine.Domain.Common;

namespace QrDine.Domain.Billing;

/// <summary>
/// The till counted at the end of a day. Expected is what the books say should be in the drawer;
/// Counted is what was actually there, so Difference is any shortage (negative) or excess.
/// </summary>
public class DayClosing : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }

    /// <summary>Zero means the whole restaurant rather than one branch.</summary>
    public int BranchId { get; set; }

    /// <summary>The Pakistan calendar day, at midnight.</summary>
    public DateTime Date { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal Expected { get; set; }
    public decimal Counted { get; set; }
    public decimal Difference { get; set; }

    /// <summary>Cash the owner took from the drawer, so tomorrow opens with what's really left.</summary>
    public decimal TakenOut { get; set; }
    public string Note { get; set; } = "";
    public string ClosedBy { get; set; } = "";
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
}
