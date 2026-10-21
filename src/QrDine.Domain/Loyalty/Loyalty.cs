using QrDine.Domain.Common;

namespace QrDine.Domain.Loyalty;

/// <summary>
/// A restaurant's loyalty programme and review links (one row per restaurant). Off until the
/// owner switches it on, so existing restaurants see no change.
/// Earning: <see cref="EarnPoints"/> points for every <see cref="PerAmount"/> of a settled bill.
/// </summary>
public class LoyaltyProgram : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public bool Enabled { get; set; } = false;
    public string Name { get; set; } = "Rewards";
    public string Tagline { get; set; } = "Earn points on every visit and treat yourself.";
    public int EarnPoints { get; set; } = 1;          // points earned…
    public decimal PerAmount { get; set; } = 100;     // …for every this much spent (Rs)
    public decimal MinBillAmount { get; set; } = 0;   // bills below this earn nothing
    public int WelcomeBonus { get; set; } = 50;       // once, when a guest joins
    public int FeedbackBonus { get; set; } = 10;      // for an in-house rating, at most once a day
    public string GoogleReviewUrl { get; set; } = ""; // "write a review" link (search.google.com/local/writereview?placeid=…)
    public string GoogleMapsUrl { get; set; } = "";   // the restaurant's Google Maps page (to read reviews)
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Points a settled bill of <paramref name="amount"/> earns under this programme.</summary>
    public int PointsFor(decimal amount)
    {
        if (!Enabled || amount <= 0 || PerAmount <= 0 || EarnPoints <= 0 || amount < MinBillAmount) return 0;
        return (int)Math.Floor(amount / PerAmount) * EarnPoints;
    }
}

/// <summary>A guest who joined the programme, known by phone number (unique per restaurant).</summary>
public class LoyaltyMember : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Phone { get; set; } = "";           // normalised digits, e.g. 03001234567
    public string Name { get; set; } = "";
    public int Points { get; set; }                   // spendable balance
    public int LifetimePoints { get; set; }           // everything ever earned (never goes down)
    public decimal LifetimeSpend { get; set; }
    public int Visits { get; set; }                   // settled bills linked to this member
    public string Token { get; set; } = "";           // remembers the member on the guest's phone
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastVisitAt { get; set; }

    /// <summary>Add (or, with a negative number, take off) points; the balance never goes below zero.</summary>
    public int Apply(int points)
    {
        var before = Points;
        Points = Math.Max(0, Points + points);
        if (points > 0) LifetimePoints += points;
        return Points - before;   // what actually changed
    }
}

/// <summary>Something a member can spend points on (a free drink, Rs 500 off…).</summary>
public class LoyaltyReward : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Emoji { get; set; } = "🎁";
    public int PointsCost { get; set; }
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum LoyaltyTxnType { Welcome, Earn, Feedback, Redeem, Adjust }

/// <summary>One line of a member's points history. The ledger explains every balance change.</summary>
public class LoyaltyTransaction : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int MemberId { get; set; }
    public LoyaltyTxnType Type { get; set; }
    public int Points { get; set; }                   // + earned, − spent/removed
    public decimal BillAmount { get; set; }
    public int? TableSessionId { get; set; }          // the seating that earned it (one Earn per seating)
    public int? InvoiceId { get; set; }
    public int? RedemptionId { get; set; }
    public string Note { get; set; } = "";
    public string CreatedBy { get; set; } = "";       // staff username, or "" for the guest/system
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum RedemptionStatus { Pending, Approved, Rejected, Cancelled }

/// <summary>
/// A member's request to spend points on a reward. Points only come off when staff approve it, so
/// a typed-in phone number alone can never spend someone's points.
/// </summary>
public class LoyaltyRedemption : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int MemberId { get; set; }
    public int RewardId { get; set; }
    public string RewardTitle { get; set; } = "";     // snapshot, so history survives a renamed/deleted reward
    public string RewardEmoji { get; set; } = "";
    public int PointsCost { get; set; }
    public RedemptionStatus Status { get; set; } = RedemptionStatus.Pending;
    public string Code { get; set; } = "";            // short code the guest shows staff
    public string TableCode { get; set; } = "";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public string DecidedBy { get; set; } = "";
    public string Note { get; set; } = "";

    public void Approve(string by)
    {
        if (Status != RedemptionStatus.Pending) throw new DomainException("This reward request was already handled.");
        Status = RedemptionStatus.Approved; DecidedAt = DateTime.UtcNow; DecidedBy = by;
    }

    public void Reject(string by, string? note)
    {
        if (Status != RedemptionStatus.Pending) throw new DomainException("This reward request was already handled.");
        Status = RedemptionStatus.Rejected; DecidedAt = DateTime.UtcNow; DecidedBy = by; Note = (note ?? "").Trim();
    }

    public void Cancel()
    {
        if (Status != RedemptionStatus.Pending) throw new DomainException("Only a waiting request can be cancelled.");
        Status = RedemptionStatus.Cancelled; DecidedAt = DateTime.UtcNow;
    }
}
