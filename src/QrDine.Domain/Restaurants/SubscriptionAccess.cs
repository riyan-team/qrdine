namespace QrDine.Domain.Restaurants;

public enum SubscriptionState
{
    Active,      // paid, nothing to say
    EndingSoon,  // paid date is close: warn the owner
    Overdue,     // paid date has passed but the grace days have not: still works, warn loudly
    Locked,      // grace days are over: the restaurant is switched off until a payment is recorded
}

/// <summary>
/// Works out what a restaurant's paid-through date means today. DaysLeft counts down to
/// the paid date while EndingSoon, and to the lock while Overdue. A restaurant with no
/// date is never locked or warned.
/// </summary>
public readonly record struct SubscriptionAccess(SubscriptionState State, int DaysLeft)
{
    public const int DefaultGraceDays = 3;
    public const int WarnDays = 5;

    public static SubscriptionAccess Evaluate(DateTime? paidThrough, DateTime nowUtc, int graceDays)
    {
        if (paidThrough is not DateTime paid) return new(SubscriptionState.Active, 0);

        var lockAt = paid.AddDays(Math.Max(0, graceDays));
        if (nowUtc >= lockAt) return new(SubscriptionState.Locked, 0);
        if (nowUtc >= paid) return new(SubscriptionState.Overdue, DaysUntil(lockAt, nowUtc));
        if (nowUtc >= paid.AddDays(-WarnDays)) return new(SubscriptionState.EndingSoon, DaysUntil(paid, nowUtc));
        return new(SubscriptionState.Active, DaysUntil(paid, nowUtc));
    }

    private static int DaysUntil(DateTime when, DateTime now) => (int)Math.Ceiling((when - now).TotalDays);
}
