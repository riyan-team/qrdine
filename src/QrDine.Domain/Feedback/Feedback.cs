using QrDine.Domain.Common;

namespace QrDine.Domain.Feedback;

/// <summary>A customer's rating (1–5) and optional comment for a visit, left from
/// the table's QR page. Tenant-owned so each restaurant only sees its own.</summary>
public class Feedback : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int Rating { get; set; }              // 1..5
    public string Comment { get; set; } = "";
    public string TableCode { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
