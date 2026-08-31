using QrDine.Domain.Common;

namespace QrDine.Domain.Restaurants;

/// <summary>Which module bundle a restaurant is subscribed to. Drives feature flags.</summary>
public enum SubscriptionPlan
{
    Trial,       // everything on, time-limited
    Menu,        // M0 — QR menu only
    Ordering,    // + self-ordering
    Operations,  // + kitchen / waiter
    Full,        // + billing / tax
}

public enum RestaurantStatus { Active, Suspended }

/// <summary>
/// The tenant root. The platform operator sets its Plan and Status; the plan
/// decides which feature flags are on, so subscriptions actually gate access.
/// </summary>
public class Restaurant : Entity
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "Rs";

    public decimal TaxPercent { get; set; } = 0;      // default GST when an item has no own rate
    public decimal ServiceChargePercent { get; set; } = 0;

    // ---- Receipt branding (printed on 80mm thermal receipts) ----
    public string LogoUrl { get; set; } = "";          // logo shown centred at the top of the receipt
    public string ReceiptHeader { get; set; } = "";    // address / phone line under the name
    public string ReceiptFooter { get; set; } = "";    // thank-you / policy line at the foot

    // Service settings. When true, approving an order auto-assigns the least-busy
    // waiter; when false (default) waiters take their own tables / a manager assigns.
    public bool WaiterAutoAssign { get; set; } = false;

    // ---- Seller identity (printed on the fiscal invoice / sent to FBR) ----
    public string BusinessName { get; set; } = "";    // registered business name (defaults to Name)
    public string Ntn { get; set; } = "";             // National Tax Number
    public string Strn { get; set; } = "";            // Sales Tax Registration Number

    // ---- FBR fiscal integration ----
    // FeatureFbr is the SaaS gate set by the platform operator; FbrEnabled is the
    // owner's own on/off switch (only usable while FeatureFbr is on).
    public bool FeatureFbr { get; set; } = false;
    public bool FbrEnabled { get; set; } = false;

    // Subscription
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Trial;   // legacy tier (kept for history)
    public int? PlanId { get; set; }                 // current catalogue plan
    public string PlanName { get; set; } = "Trial";
    public RestaurantStatus Status { get; set; } = RestaurantStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PlanExpiresAt { get; set; }      // paid-through date (monthly)

    // ---- Subscription billing (operator records payments manually) ----
    public bool SetupFeePaid { get; set; } = false;   // one-time activation fee settled
    public DateTime? LastPaymentAt { get; set; }

    // Feature flags — derived from the plan.
    public bool FeatureOrdering { get; set; } = true;
    public bool FeatureKitchen { get; set; } = true;
    public bool FeatureWaiter { get; set; } = true;
    public bool FeatureBilling { get; set; } = true;
    public bool FeatureInventory { get; set; } = true;
    public bool FeatureReports { get; set; } = true;

    /// <summary>Put this restaurant on a catalogue plan and copy its features.</summary>
    public void AssignPlan(Subscriptions.Plan plan)
    {
        PlanId = plan.Id;
        PlanName = plan.Name;
        FeatureOrdering = plan.Ordering;
        FeatureKitchen = plan.Kitchen;
        FeatureWaiter = plan.Waiter;
        FeatureBilling = plan.Billing;
        FeatureInventory = plan.Inventory;
        FeatureReports = plan.Reports;
    }

    /// <summary>Set the plan and turn feature flags on/off to match it.</summary>
    public void ApplyPlan(SubscriptionPlan plan)
    {
        Plan = plan;
        switch (plan)
        {
            case SubscriptionPlan.Menu:
                FeatureOrdering = FeatureKitchen = FeatureWaiter = FeatureBilling = false; break;
            case SubscriptionPlan.Ordering:
                FeatureOrdering = true; FeatureKitchen = FeatureWaiter = FeatureBilling = false; break;
            case SubscriptionPlan.Operations:
                FeatureOrdering = FeatureKitchen = FeatureWaiter = true; FeatureBilling = false; break;
            case SubscriptionPlan.Full:
            case SubscriptionPlan.Trial:
            default:
                FeatureOrdering = FeatureKitchen = FeatureWaiter = FeatureBilling = true; break;
        }
    }
}
