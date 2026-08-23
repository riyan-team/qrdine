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

    // ---- Deal / offer (optional discounted price, with an optional schedule) ----
    public decimal? DealPrice { get; set; }        // discounted price; null = no deal
    public string DealDescription { get; set; } = "";  // "what's included" for combos
    public string DealDays { get; set; } = "";     // "" = every day; else CSV of 0-6 (0=Sunday)
    public int? DealStartMinutes { get; set; }     // window start, PKT minutes from midnight; null = all day
    public int? DealEndMinutes { get; set; }
    public bool DealHideWhenOff { get; set; }      // hide the item entirely outside the deal window

    /// <summary>Is the deal price in effect right now (given UTC time, evaluated in
    /// Pakistan time = UTC+5)? False if there's no deal, or we're outside its
    /// day/time window.</summary>
    public bool IsDealActive(DateTime utcNow)
    {
        if (DealPrice is null || DealPrice.Value < 0) return false;
        var pkt = utcNow.AddHours(5);
        if (!string.IsNullOrWhiteSpace(DealDays))
        {
            var today = (int)pkt.DayOfWeek;   // 0 = Sunday
            var ok = false;
            foreach (var part in DealDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out var d) && d == today) { ok = true; break; }
            if (!ok) return false;
        }
        if (DealStartMinutes is int s && DealEndMinutes is int e && s != e)
        {
            var mins = pkt.Hour * 60 + pkt.Minute;
            var inWindow = s < e ? (mins >= s && mins < e) : (mins >= s || mins < e);  // s>e = overnight window
            if (!inWindow) return false;
        }
        return true;
    }

    public List<MenuItemVariant> Variants { get; set; } = new();
    public List<ModifierGroup> ModifierGroups { get; set; } = new();
    public List<Inventory.RecipeLine> Recipe { get; set; } = new();
}
