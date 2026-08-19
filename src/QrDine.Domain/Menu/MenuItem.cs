using QrDine.Domain.Common;

namespace QrDine.Domain.Menu;

public class MenuItem : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string NameUrdu { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Emoji { get; set; } = "🍽️";
    public string? ImageUrl { get; set; }        // uploaded photo, if any
    public bool Available { get; set; } = true;

    // ---- Tax (per item, owner-managed) ----
    // TaxRate is this item's sales-tax %. Null means "use the restaurant default".
    // PctCode is the FBR Pakistan Customs Tariff / HS code required on each line.
    public decimal? TaxRate { get; set; }
    public decimal FurtherTaxRate { get; set; }  // optional further tax %, usually 0
    public string PctCode { get; set; } = "";
    public int PrepMinutes { get; set; } = 15;   // default ETA; the kitchen can override per order

    public List<MenuItemVariant> Variants { get; set; } = new();
    public List<ModifierGroup> ModifierGroups { get; set; } = new();
    public List<Inventory.RecipeLine> Recipe { get; set; } = new();
}
