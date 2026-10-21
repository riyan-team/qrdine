namespace QrDine.Application.Loyalty;

// ---- guest (QR page) ----
public record LoyaltyJoinInput(string? Name, string Phone, string? TableCode);
public record LoyaltyRedeemInput(int RewardId, string? TableCode);
public record LoyaltyLinkInput(string TableCode);

public record LoyaltyRewardDto(int Id, string Title, string Description, string Emoji, int PointsCost, bool Active, int SortOrder);
public record ReviewStatsDto(double Average, int Count);

/// <summary>What anyone scanning the QR sees: the programme, its rewards and the review links.</summary>
public record LoyaltyPublicDto(bool Enabled, string Name, string Tagline, int EarnPoints, decimal PerAmount,
    decimal MinBillAmount, int WelcomeBonus, int FeedbackBonus, string GoogleReviewUrl, string GoogleMapsUrl,
    string Currency, IReadOnlyList<LoyaltyRewardDto> Rewards, ReviewStatsDto Reviews);

public record LoyaltyTxnDto(int Id, string Type, int Points, decimal BillAmount, string Note, DateTime CreatedAt);

public record LoyaltyRedemptionDto(int Id, string RewardTitle, string RewardEmoji, int PointsCost, string Status,
    string Code, string TableCode, DateTime RequestedAt, DateTime? DecidedAt, string DecidedBy, string Note,
    int MemberId, string MemberName, string MemberPhone);

/// <summary>A member's own view: balance, waiting requests and recent history.</summary>
public record LoyaltyMeDto(string Token, string Name, string PhoneMasked, int Points, int AvailablePoints,
    int LifetimePoints, int Visits, DateTime JoinedAt, bool JustJoined, int WelcomeBonusAwarded,
    IReadOnlyList<LoyaltyRedemptionDto> Redemptions, IReadOnlyList<LoyaltyTxnDto> History);

// ---- admin ----
public record LoyaltyProgramInput(bool Enabled, string Name, string? Tagline, int EarnPoints, decimal PerAmount,
    decimal MinBillAmount, int WelcomeBonus, int FeedbackBonus, string? GoogleReviewUrl, string? GoogleMapsUrl);

public record LoyaltyProgramDto(bool Enabled, string Name, string Tagline, int EarnPoints, decimal PerAmount,
    decimal MinBillAmount, int WelcomeBonus, int FeedbackBonus, string GoogleReviewUrl, string GoogleMapsUrl,
    string Currency, DateTime UpdatedAt);

public record LoyaltyRewardInput(string Title, string? Description, string? Emoji, int PointsCost, bool Active, int SortOrder);
public record LoyaltyAdjustInput(int Points, string? Note);
public record LoyaltyAttachInput(string Phone, string? Name);
public record LoyaltyDecisionInput(string? Note);

public record LoyaltyMemberDto(int Id, string Name, string Phone, int Points, int LifetimePoints, decimal LifetimeSpend,
    int Visits, DateTime JoinedAt, DateTime? LastVisitAt);
public record LoyaltyMemberDetailDto(LoyaltyMemberDto Member, IReadOnlyList<LoyaltyTxnDto> History, IReadOnlyList<LoyaltyRedemptionDto> Redemptions);
public record LoyaltyMembersPageDto(IReadOnlyList<LoyaltyMemberDto> Items, int Total, int Page, int PageSize);

public record LoyaltyOverviewDto(bool Enabled, int Members, int ActiveMembers30d, int NewMembers30d, int PointsIssued,
    int PointsRedeemed, int PointsOutstanding, int PendingRedemptions, int ApprovedRedemptions, ReviewStatsDto Reviews,
    IReadOnlyList<LoyaltyMemberDto> TopMembers);

/// <summary>Reception's view of a running bill: who it earns points for, and their waiting reward requests.</summary>
/// The earn rule rides along so the screen can say "this bill earns about N points" from the bill it already shows.
public record TableMemberDto(bool Enabled, LoyaltyMemberDto? Member, IReadOnlyList<LoyaltyRedemptionDto> Pending,
    int EarnPoints, decimal PerAmount, decimal MinBillAmount);
