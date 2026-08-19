using QrDine.Domain.Common;

namespace QrDine.Domain.Subscriptions;

/// <summary>
/// A subscription plan in the platform catalogue. The operator creates these and
/// picks which features each one includes; a restaurant on this plan gets exactly
/// those features. Data-driven, so new plans need no code change.
/// </summary>
public class Plan : Entity
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal PricePkr { get; set; }          // per month
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Included features (the "multiple selection").
    public bool Ordering { get; set; }
    public bool Kitchen { get; set; }
    public bool Waiter { get; set; }
    public bool Billing { get; set; }
    public bool Inventory { get; set; }
    public bool Reports { get; set; }
}
