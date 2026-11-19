using QrDine.Domain.Restaurants;
using Xunit;

namespace QrDine.Tests;

/// <summary>When a restaurant gets warned, and when it gets locked for not paying.</summary>
public class SubscriptionAccessTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private const int Grace = 3;

    private static SubscriptionAccess At(double daysFromNow) =>
        SubscriptionAccess.Evaluate(Now.AddDays(daysFromNow), Now, Grace);

    [Fact]
    public void No_date_is_never_locked()
    {
        var a = SubscriptionAccess.Evaluate(null, Now, Grace);
        Assert.Equal(SubscriptionState.Active, a.State);
    }

    [Fact]
    public void Well_before_the_date_nothing_is_said()
    {
        Assert.Equal(SubscriptionState.Active, At(6).State);
    }

    [Fact]
    public void Five_days_before_the_date_the_owner_is_warned()
    {
        var a = At(5);
        Assert.Equal(SubscriptionState.EndingSoon, a.State);
        Assert.Equal(5, a.DaysLeft);
    }

    [Fact]
    public void Part_of_a_day_left_counts_as_one_day()
    {
        Assert.Equal(1, At(0.25).DaysLeft);
    }

    [Fact]
    public void Just_after_the_date_it_still_works_and_counts_down_to_the_lock()
    {
        var a = At(-0.5);
        Assert.Equal(SubscriptionState.Overdue, a.State);
        Assert.Equal(3, a.DaysLeft);
    }

    [Fact]
    public void Last_moment_of_the_grace_days_still_works()
    {
        Assert.Equal(SubscriptionState.Overdue, At(-2.99).State);
    }

    [Fact]
    public void Once_the_grace_days_end_it_pauses_at_the_next_5am_never_during_service()
    {
        // Grace ran out at 5 pm Pakistan time (12:00 UTC): tonight's service carries on.
        Assert.Equal(SubscriptionState.Overdue, At(-3).State);
        var paid = Now.AddDays(-3);
        Assert.Equal(SubscriptionState.Overdue, SubscriptionAccess.Evaluate(paid, Now.Date.AddDays(1).AddMinutes(-1), Grace).State);   // 4:59 am
        Assert.Equal(SubscriptionState.Locked, SubscriptionAccess.Evaluate(paid, Now.Date.AddDays(1), Grace).State);                 // 5:00 am
        Assert.Equal(SubscriptionState.Locked, At(-30).State);
    }

    [Fact]
    public void Zero_grace_pauses_at_the_first_5am_after_the_date()
    {
        Assert.Equal(SubscriptionState.Overdue, SubscriptionAccess.Evaluate(Now.AddMinutes(-1), Now, 0).State);
        Assert.Equal(SubscriptionState.Locked, SubscriptionAccess.Evaluate(Now.AddMinutes(-1), Now.Date.AddDays(1), 0).State);
    }
}
