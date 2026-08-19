using QrDine.Domain.Common;

namespace QrDine.Domain.Menu;

public class MenuCategory : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public List<MenuItem> Items { get; set; } = new();
}
