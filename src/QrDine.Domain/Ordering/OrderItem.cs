using QrDine.Domain.Common;

namespace QrDine.Domain.Ordering;

/// <summary>
/// A single line on an order. Name, VariantName, Price and the chosen modifiers
/// are copied in at order time — a later menu change must never rewrite a bill
/// that was already placed. Price is the effective unit price (chosen variant
/// plus any modifier add-ons), so LineTotal stays a simple Price × Qty.
/// </summary>
public class OrderItem : Entity
{
    public int OrderId { get; set; }
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public string? VariantName { get; set; }     // e.g. "Full"
    public decimal Price { get; set; }           // effective unit price incl. modifiers
    public int Qty { get; set; } = 1;
    public string? Note { get; set; }            // e.g. "no onion", "extra spicy"

    public List<OrderItemModifier> Modifiers { get; set; } = new();

    public decimal LineTotal => Price * Qty;
}

/// <summary>A chosen add-on, snapshotted for display on the ticket and bill.</summary>
public class OrderItemModifier : Entity
{
    public int OrderItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
