using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Loyalty;
using QrDine.Domain.Common;
using QrDine.Domain.Loyalty;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Tables;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// Loyalty rules against a real (in-memory SQLite) DbContext, so the tenant filter and persistence are
/// exercised: joining, earning once per seating, spending only on staff approval, and the bonuses.
/// </summary>
public class LoyaltyTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly StubTenant _tenant = new();
    private readonly LoyaltyService _svc;

    public LoyaltyTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(opts, _tenant);
        _db.Database.EnsureCreated();
        _svc = new LoyaltyService(_db, _tenant);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    /// <summary>Restaurant 1 with an open seating at table T1 (id returned), and a programme: 1 point per Rs 100.</summary>
    private async Task<int> Seed(bool enabled = true, int welcome = 50, int feedback = 10, decimal minBill = 0)
    {
        _db.Restaurants.Add(new Restaurant { Slug = "test", Name = "Test" });
        await _db.SaveChangesAsync();
        _db.Branches.Add(new Branch { RestaurantId = 1, Name = "Main" });
        _db.Tables.Add(new DiningTable { RestaurantId = 1, BranchId = 1, Code = "T1", Name = "Table 1" });
        await _db.SaveChangesAsync();
        var session = TableSession.Open(await _db.Tables.FirstAsync());
        _db.Sessions.Add(session);
        await _svc.UpdateProgram(new LoyaltyProgramInput(enabled, "Rewards", null, 1, 100, minBill, welcome, feedback, null, null));
        await _db.SaveChangesAsync();
        return session.Id;
    }

    [Theory]
    [InlineData("0300 1234567", "03001234567")]
    [InlineData("+92 300 1234567", "03001234567")]
    [InlineData("923001234567", "03001234567")]
    [InlineData("00923001234567", "03001234567")]
    [InlineData("3001234567", "03001234567")]
    [InlineData("+44 7700 900123", "447700900123")]
    public void Phone_numbers_are_normalised(string raw, string expected) =>
        Assert.Equal(expected, LoyaltyService.NormalizePhone(raw));

    [Fact]
    public void A_short_phone_number_is_refused() =>
        Assert.Throws<DomainException>(() => LoyaltyService.NormalizePhone("12345"));

    [Fact]
    public void Earn_rule_counts_whole_steps_and_respects_the_minimum()
    {
        var p = new LoyaltyProgram { Enabled = true, EarnPoints = 2, PerAmount = 100, MinBillAmount = 500 };
        Assert.Equal(48, p.PointsFor(2450m));   // 24 steps × 2
        Assert.Equal(0, p.PointsFor(499m));      // below the minimum bill
        p.Enabled = false;
        Assert.Equal(0, p.PointsFor(5000m));     // programme off
    }

    [Fact]
    public async Task Joining_gives_the_welcome_bonus_once_and_keeps_the_first_name()
    {
        await Seed();
        var first = await _svc.Join(new LoyaltyJoinInput("Ayesha", "0300 1234567", null));
        Assert.True(first.JustJoined);
        Assert.Equal(50, first.Points);
        Assert.Equal(50, first.WelcomeBonusAwarded);
        Assert.Equal("0300••••567", first.PhoneMasked);

        // the same number typed another way is the same member: no second bonus, name not overwritten
        var again = await _svc.Join(new LoyaltyJoinInput("Someone else", "+92 300 1234567", null));
        Assert.False(again.JustJoined);
        Assert.Equal(first.Token, again.Token);
        Assert.Equal("Ayesha", again.Name);
        Assert.Equal(50, again.Points);
        Assert.Equal(1, await _db.LoyaltyMembers.CountAsync());
        Assert.Equal(1, await _db.LoyaltyTransactions.CountAsync(t => t.Type == LoyaltyTxnType.Welcome));
    }

    [Fact]
    public async Task Joining_is_refused_while_the_programme_is_off()
    {
        await Seed(enabled: false);
        await Assert.ThrowsAsync<DomainException>(() => _svc.Join(new LoyaltyJoinInput("A", "03001234567", null)));
    }

    [Fact]
    public async Task A_settled_seating_earns_once_and_counts_the_visit()
    {
        var sessionId = await Seed();
        var me = await _svc.Join(new LoyaltyJoinInput("Bilal", "03001112223", "T1"));   // links the open seating

        Assert.Equal(24, await _svc.AwardForSettledSession(sessionId, 7, 2450m));
        Assert.Equal(0, await _svc.AwardForSettledSession(sessionId, 7, 2450m));        // a retry earns nothing

        var m = await _db.LoyaltyMembers.AsNoTracking().FirstAsync();
        Assert.Equal(50 + 24, m.Points);
        Assert.Equal(1, m.Visits);
        Assert.Equal(2450m, m.LifetimeSpend);
        Assert.Equal(me.Token, m.Token);
    }

    [Fact]
    public async Task A_seating_without_a_member_earns_nothing()
    {
        var sessionId = await Seed();
        Assert.Equal(0, await _svc.AwardForSettledSession(sessionId, null, 5000m));
        Assert.Equal(0, await _db.LoyaltyTransactions.CountAsync());
    }

    [Fact]
    public async Task Points_come_off_only_when_staff_approve()
    {
        await Seed(welcome: 100);
        var me = await _svc.Join(new LoyaltyJoinInput("Sana", "03005556667", null));
        var drink = await _svc.SaveReward(null, new LoyaltyRewardInput("Free drink", null, "🥤", 80, true, 0));
        var dessert = await _svc.SaveReward(null, new LoyaltyRewardInput("Dessert", null, "🍰", 150, true, 1));

        await Assert.ThrowsAsync<DomainException>(() => _svc.Redeem(me.Token, new LoyaltyRedeemInput(dessert.Id, "T1")));   // 150 > 100

        var req = await _svc.Redeem(me.Token, new LoyaltyRedeemInput(drink.Id, "T1"));
        Assert.Equal("Pending", req.Status);
        Assert.Equal(6, req.Code.Length);
        var waiting = await _svc.Me(me.Token);
        Assert.Equal(100, waiting!.Points);          // nothing taken yet…
        Assert.Equal(20, waiting.AvailablePoints);   // …but the waiting request is set aside

        await Assert.ThrowsAsync<DomainException>(() => _svc.Redeem(me.Token, new LoyaltyRedeemInput(drink.Id, "T1")));     // 80 > 20 left

        var done = await _svc.Approve(req.Id, "cashier");
        Assert.Equal("Approved", done.Status);
        Assert.Equal(20, (await _svc.Me(me.Token))!.Points);
        await Assert.ThrowsAsync<DomainException>(() => _svc.Approve(req.Id, "cashier"));   // handled already
    }

    [Fact]
    public async Task A_rejected_or_cancelled_request_keeps_the_points()
    {
        await Seed(welcome: 100);
        var me = await _svc.Join(new LoyaltyJoinInput("Omar", "03007778889", null));
        var reward = await _svc.SaveReward(null, new LoyaltyRewardInput("Fries", null, null, 60, true, 0));

        var r1 = await _svc.Redeem(me.Token, new LoyaltyRedeemInput(reward.Id, null));
        await _svc.Reject(r1.Id, "manager", "Not today");
        var r2 = await _svc.Redeem(me.Token, new LoyaltyRedeemInput(reward.Id, null));
        await _svc.CancelRedemption(me.Token, r2.Id);

        var now = await _svc.Me(me.Token);
        Assert.Equal(100, now!.Points);
        Assert.Equal(100, now.AvailablePoints);
    }

    [Fact]
    public async Task The_feedback_bonus_is_given_at_most_once_a_day()
    {
        await Seed(welcome: 0, feedback: 10);
        var me = await _svc.Join(new LoyaltyJoinInput("Hina", "03009990001", null));
        Assert.Equal(10, await _svc.AwardFeedback(me.Token));
        Assert.Equal(0, await _svc.AwardFeedback(me.Token));
        Assert.Equal(0, await _svc.AwardFeedback("not-a-member-token-at-all"));
        Assert.Equal(10, (await _svc.Me(me.Token))!.Points);
    }

    [Fact]
    public async Task Reception_can_attach_a_member_to_a_running_bill()
    {
        var sessionId = await Seed(welcome: 0);
        var view = await _svc.AttachToTable("T1", new LoyaltyAttachInput("0321 4445556", "Walk-in"), "cashier");
        Assert.NotNull(view.Member);
        Assert.Equal("03214445556", view.Member!.Phone);
        Assert.Equal(10, await _svc.AwardForSettledSession(sessionId, null, 1000m));
    }

    [Fact]
    public async Task Members_are_private_to_their_restaurant()
    {
        await Seed();
        await _svc.Join(new LoyaltyJoinInput("Ali", "03001234567", null));

        _db.Restaurants.Add(new Restaurant { Slug = "other", Name = "Other" });
        await _db.SaveChangesAsync();
        _tenant.Set(2, "other");
        Assert.Equal(0, await _db.LoyaltyMembers.CountAsync());
        Assert.Equal(0, (await _svc.Members(null, 1, 20)).Total);
    }
}
