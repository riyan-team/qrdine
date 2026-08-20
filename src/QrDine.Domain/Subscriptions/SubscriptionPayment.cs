using QrDine.Domain.Common;

namespace QrDine.Domain.Subscriptions;

public enum SubscriptionPaymentKind { Setup, Monthly }

/// <summary>
/// A subscription payment the platform operator recorded for a restaurant (collected
/// offline via bank/JazzCash and confirmed manually). Setup = the one-time activation
/// fee; Monthly = N months of the recurring fee, which extends the paid-through date.
/// Platform-managed — NOT tenant-owned (the operator sees every restaurant's history).
/// </summary>
public class SubscriptionPayment : Entity
{
    public int RestaurantId { get; set; }
    public SubscriptionPaymentKind Kind { get; set; }
    public int Months { get; set; }                 // 0 for Setup
    public decimal AmountPkr { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public DateTime? NewPaidThrough { get; set; }   // paid-through date after applying this
    public string Note { get; set; } = "";
    public string RecordedBy { get; set; } = "";
}
