using QrDine.Domain.Common;

namespace QrDine.Domain.Inventory;

/// <summary>A tracked ingredient/supply (e.g. Chicken, Naan flour, Cola bottles).</summary>
public class StockItem : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "unit";     // kg, litre, pcs …
    public decimal Quantity { get; set; }
    public decimal LowThreshold { get; set; }       // alert at or below this
    public decimal LastCost { get; set; }           // per unit, from the latest supplier bill

    public bool IsLow => Quantity <= LowThreshold;
}

public enum StockMovementType { Restock, Adjust, Sale, Void, Waste }

/// <summary>An audit record of every change to a stock item's quantity.</summary>
public class StockMovement : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int StockItemId { get; set; }
    public decimal Delta { get; set; }              // + added, - consumed
    public StockMovementType Type { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>How much of a stock item one serving of a menu item consumes.</summary>
public class RecipeLine : Entity
{
    public int MenuItemId { get; set; }
    public int StockItemId { get; set; }
    public decimal QtyPerServing { get; set; }
}
