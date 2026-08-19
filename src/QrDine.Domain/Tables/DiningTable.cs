using QrDine.Domain.Common;

namespace QrDine.Domain.Tables;

/// <summary>A physical table. Its Code is printed into the QR sticker.</summary>
public class DiningTable : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public string Code { get; set; } = "";   // e.g. "T1"
    public string Name { get; set; } = "";   // e.g. "Table 1"
}
