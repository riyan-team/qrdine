using QrDine.Domain.Common;

namespace QrDine.Domain.Purchasing;

/// <summary>Someone the restaurant buys from on credit: the sabzi wala, the butcher, the Coke dealer.</summary>
public class Supplier : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>What the restaurant already owed them when this khata was started.</summary>
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum SupplierEntryType { Purchase, Payment, Return, Adjustment }

/// <summary>One line in a supplier's khata. <see cref="Amount"/> is always what moved, and
/// <see cref="BalanceEffect"/> says which way it moved what the restaurant owes.</summary>
public class SupplierEntry : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int SupplierId { get; set; }
    public SupplierEntryType Type { get; set; }

    /// <summary>The Pakistan calendar day this belongs to, at midnight (no time zone).</summary>
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";     // the supplier's bill number
    public string Note { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The payment made at the same time as this purchase, so deleting one removes both.</summary>
    public int? LinkedEntryId { get; set; }

    public List<SupplierEntryLine> Lines { get; set; } = new();

    public decimal BalanceEffect => Type switch
    {
        SupplierEntryType.Purchase => Amount,
        SupplierEntryType.Adjustment => Amount,     // may be negative
        _ => -Amount,
    };
}

/// <summary>What was on a bill. A line tied to a stock item moves that stock.</summary>
public class SupplierEntryLine : Entity
{
    public int SupplierEntryId { get; set; }
    public int? StockItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }

    public decimal LineTotal => Qty * UnitCost;
}
