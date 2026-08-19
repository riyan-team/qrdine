using QrDine.Domain.Common;

namespace QrDine.Domain.Menu;

/// <summary>
/// A set of add-on choices for an item (e.g. "Spice level", "Add-ons").
/// MinSelect/MaxSelect express the rule: spice level is exactly one (1..1);
/// optional add-ons are 0..N.
/// </summary>
public class ModifierGroup : Entity
{
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public int MinSelect { get; set; }       // 0 = optional
    public int MaxSelect { get; set; } = 1;
    public int SortOrder { get; set; }
    public List<ModifierOption> Options { get; set; } = new();
}

/// <summary>One selectable add-on within a group; its price adds to the line.</summary>
public class ModifierOption : Entity
{
    public int ModifierGroupId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }       // 0 for free choices like spice level
    public int SortOrder { get; set; }
}
