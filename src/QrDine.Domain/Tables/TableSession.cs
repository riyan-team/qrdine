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

    // Parcel and delivery only: who it's for, and what they pay to have it brought.
    public Ordering.OrderChannel Channel { get; set; } = Ordering.OrderChannel.Dine;
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string DeliveryAddress { get; set; } = "";
    public string RiderName { get; set; } = "";
    public decimal DeliveryCharge { get; set; }
    public int? CustomerId { get; set; }

    // Delivery handling: where (the guest's map pin), when (the time promised), and the rider's trip.
    public double? DeliveryLat { get; set; }
    public double? DeliveryLng { get; set; }
    public DateTime? PromisedAt { get; set; }      // delivery: at the door by; takeaway: ready by
    public DateTime? DispatchedAt { get; set; }    // the rider left with it
    public DateTime? DeliveredAt { get; set; }     // handed over (delivery) or collected (takeaway)
    public string RiderPhone { get; set; } = "";
    public string RiderKey { get; set; } = "";     // the secret in the rider's link; a new link replaces it
    public double? RiderLat { get; set; }
    public double? RiderLng { get; set; }
    public DateTime? RiderSeenAt { get; set; }

    public static TableSession Open(DiningTable table) => new()
    {
        RestaurantId = table.RestaurantId,
        BranchId = table.BranchId,
        DiningTableId = table.Id,
        TableCode = table.Code,
        TableName = table.Name,
        Channel = table.Kind,
        IsOpen = true,
        OpenedAt = DateTime.UtcNow,
    };

    public void Close()
    {
        IsOpen = false;
        ClosedAt = DateTime.UtcNow;
    }
}
