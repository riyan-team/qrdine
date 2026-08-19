namespace QrDine.Domain.Ordering;

/// <summary>
/// The order lifecycle. Transitions are enforced by the Order aggregate —
/// each step is only legal from the one before it.
/// Placed → Approved → Cooking → Ready → Served → Paid  (+ Cancelled early)
/// </summary>
public enum OrderStatus
{
    Placed,
    Approved,
    Cooking,
    Ready,
    Served,
    Paid,
    Cancelled,
}
