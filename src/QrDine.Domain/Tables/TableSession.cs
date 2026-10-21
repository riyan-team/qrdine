using QrDine.Domain.Common;

namespace QrDine.Domain.Tables;

/// <summary>
/// One seating at a table. Opens when the first order is placed and closes when
/// the bill is paid. This is the boundary between one group of guests and the
/// next — without it, a new customer scanning the QR would see the previous
/// diner's orders and bill.
/// </summary>
public class TableSession : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public int DiningTableId { get; set; }
    public string TableCode { get; set; } = "";
    public string TableName { get; set; } = "";
    public bool IsOpen { get; set; } = true;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    /// <summary>The loyalty member this seating's bill earns points for (set from the guest's phone or by
    /// reception). Empty for most seatings; never affects the bill.</summary>
    public int? LoyaltyMemberId { get; set; }

    public static TableSession Open(DiningTable table) => new()
    {
        RestaurantId = table.RestaurantId,
        BranchId = table.BranchId,
        DiningTableId = table.Id,
        TableCode = table.Code,
        TableName = table.Name,
        IsOpen = true,
        OpenedAt = DateTime.UtcNow,
    };

    public void Close()
    {
        IsOpen = false;
        ClosedAt = DateTime.UtcNow;
    }
}
