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

/// <summary>
/// Money given back from a table's part payments before the bill was settled (a dish came off after the guest had
/// already paid towards it). The part payments are reduced by this much, so the drawer and the bill agree, and
/// this record keeps who gave it back and why for the owner.
/// </summary>
public class PaymentReturn : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public int TableSessionId { get; set; }
    public string TableName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public string Reason { get; set; } = "";
    public string GivenBy { get; set; } = "";
    public DateTime GivenAt { get; set; } = DateTime.UtcNow;
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
