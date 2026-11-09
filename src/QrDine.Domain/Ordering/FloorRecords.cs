using QrDine.Domain.Common;

namespace QrDine.Domain.Ordering;

/// <summary>A dish taken off a running order, kept so the owner can see every removal and who made it.</summary>
public class OrderVoid : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public int OrderId { get; set; }
    public string TableName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public string Reason { get; set; } = "";
    public string VoidedBy { get; set; } = "";
    public DateTime VoidedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A guest tapped "call waiter" or "ask for the bill" on their phone.</summary>
public class ServiceRequest : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public string TableCode { get; set; } = "";
    public string TableName { get; set; } = "";

    /// <summary>Waiter or Bill.</summary>
    public string Kind { get; set; } = "Waiter";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DoneAt { get; set; }
    public string DoneBy { get; set; } = "";
}
