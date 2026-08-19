using QrDine.Domain.Common;

namespace QrDine.Domain.Menu;

/// <summary>
/// A mutually-exclusive portion/size of a menu item (e.g. Half / Full karahi).
/// If an item has variants, the customer must pick exactly one, and the chosen
/// variant's price replaces the item's base price.
/// </summary>
public class MenuItemVariant : Entity
{
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";   // "Half", "Full", "Large"
    public decimal Price { get; set; }
    public int SortOrder { get; set; }
}
