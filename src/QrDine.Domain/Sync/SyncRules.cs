namespace QrDine.Domain.Sync;

/// <summary>Where a change was made: the live server, or a restaurant's own PC.</summary>
public enum SyncSide { Server, Device }

/// <summary>How a synced type settles a change made on the other side.</summary>
public enum SyncRule
{
    /// <summary>Settings-like rows (menu, prices, tables, staff, khata accounts): the newest change wins.</summary>
    NewestWins,

    /// <summary>
    /// Money and floor records (orders, seatings, bills, payments, khata lines, expenses…): made in
    /// one place, and only that place may change them later. The other side never overwrites them,
    /// so the same bill can't be changed in two places at once.
    /// </summary>
    OwnedByOrigin,

    /// <summary>
    /// Ledger lines (stock movements, loyalty points): written once, never edited. The totals
    /// they add up to (stock on hand, a member's points) are rebuilt from the lines on each side,
    /// never copied, so +5 here and -2 there add up instead of overwriting each other.
    /// </summary>
    Ledger,
}

public enum SyncDecision { Apply, Skip }

/// <summary>
/// The rules for settling changes between the live server and a restaurant's PC. Phase 1 writes
/// them down and tests them; the PC app's sync (phase 3) applies inbox lines through them.
/// </summary>
public static class SyncRules
{
    private static readonly Dictionary<string, SyncRule> Rules = new()
    {
        ["Restaurant"] = SyncRule.NewestWins,
        ["Branch"] = SyncRule.NewestWins,
        ["DiningTable"] = SyncRule.NewestWins,
        ["MenuCategory"] = SyncRule.NewestWins,
        ["MenuItem"] = SyncRule.NewestWins,
        ["StaffMember"] = SyncRule.NewestWins,
        ["RolePermission"] = SyncRule.NewestWins,
        ["StockItem"] = SyncRule.NewestWins,
        ["Supplier"] = SyncRule.NewestWins,
        ["Customer"] = SyncRule.NewestWins,
        ["Employee"] = SyncRule.NewestWins,
        ["AttendanceMark"] = SyncRule.NewestWins,
        ["LoyaltyProgram"] = SyncRule.NewestWins,
        ["LoyaltyReward"] = SyncRule.NewestWins,
        ["LoyaltyMember"] = SyncRule.NewestWins,

        ["Order"] = SyncRule.OwnedByOrigin,
        ["TableSession"] = SyncRule.OwnedByOrigin,
        ["Invoice"] = SyncRule.OwnedByOrigin,
        ["Payment"] = SyncRule.OwnedByOrigin,
        ["OrderVoid"] = SyncRule.OwnedByOrigin,
        ["PaymentReturn"] = SyncRule.OwnedByOrigin,
        ["ServiceRequest"] = SyncRule.OwnedByOrigin,
        ["DayClosing"] = SyncRule.OwnedByOrigin,
        ["CustomerEntry"] = SyncRule.OwnedByOrigin,
        ["SupplierEntry"] = SyncRule.OwnedByOrigin,
        ["EmployeeEntry"] = SyncRule.OwnedByOrigin,
        ["Expense"] = SyncRule.OwnedByOrigin,
        ["Feedback"] = SyncRule.OwnedByOrigin,
        ["LoyaltyRedemption"] = SyncRule.OwnedByOrigin,

        ["StockMovement"] = SyncRule.Ledger,
        ["LoyaltyTransaction"] = SyncRule.Ledger,
    };

    /// <summary>Every type that has a rule. A synced type without one is a mistake (a test checks this).</summary>
    public static IReadOnlyCollection<string> Types => Rules.Keys;

    public static SyncRule For(string entityType) =>
        Rules.TryGetValue(entityType, out var rule) ? rule
            : throw new ArgumentException($"No sync rule for {entityType}.", nameof(entityType));

    /// <summary>
    /// Should an incoming change be applied over what this side already has?
    /// <paramref name="existingOrigin"/> and <paramref name="existingUpdatedAt"/> describe this side's copy;
    /// pass <c>exists: false</c> when there is none yet.
    /// </summary>
    public static SyncDecision Decide(SyncRule rule, bool exists, SyncSide existingOrigin, DateTime? existingUpdatedAt,
        SyncSide incomingFrom, DateTime incomingChangedAt)
    {
        if (!exists) return SyncDecision.Apply;
        bool newer = existingUpdatedAt is null || incomingChangedAt > existingUpdatedAt;
        return rule switch
        {
            SyncRule.Ledger => SyncDecision.Skip,                                   // lines never change; a resend is a no-op
            SyncRule.OwnedByOrigin => incomingFrom == existingOrigin && newer ? SyncDecision.Apply : SyncDecision.Skip,
            _ => newer ? SyncDecision.Apply : SyncDecision.Skip,                    // a tie keeps what this side has
        };
    }

    /// <summary>
    /// Fields a device's change may never set: the subscription and plan (only the platform console
    /// sets those) and totals that are rebuilt from ledger lines.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ServerOnlyFields = new Dictionary<string, string[]>
    {
        ["Restaurant"] = new[]
        {
            "Slug", "Plan", "PlanId", "PlanName", "Status", "CreatedAt", "PlanExpiresAt", "SetupFeePaid", "LastPaymentAt",
            "FeatureFbr", "FeatureOrdering", "FeatureKitchen", "FeatureWaiter", "FeatureBilling", "FeatureInventory", "FeatureReports",
        },
        ["StockItem"] = new[] { "Quantity" },
        ["LoyaltyMember"] = new[] { "Points", "LifetimePoints", "LifetimeSpend", "Visits" },
    };

    /// <summary>
    /// Secrets that never leave the server in a sync answer: sign-in hashes, FBR tokens, a member's
    /// phone pass and a rider's link key. How staff sign in on the PC while offline is decided with
    /// the PC app (phase 2).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> SecretFields = new Dictionary<string, string[]>
    {
        ["StaffMember"] = new[] { "PasswordHash", "FbrToken" },
        ["Branch"] = new[] { "FbrToken" },
        ["LoyaltyMember"] = new[] { "Token" },
        ["TableSession"] = new[] { "RiderKey" },
    };

    public static bool IsSecret(string entityType, string field) =>
        SecretFields.TryGetValue(entityType, out var f) && f.Contains(field);

    /// <summary>
    /// Who gives out bill numbers. Today only the server does. Once a restaurant pairs a PC that
    /// runs its counter, only that PC does, so offline bills can't repeat a number the server used.
    /// Not switched on in phase 1: nothing calls this yet.
    /// </summary>
    public static SyncSide BillNumbersFrom(bool restaurantHasActivePc) => restaurantHasActivePc ? SyncSide.Device : SyncSide.Server;
}
