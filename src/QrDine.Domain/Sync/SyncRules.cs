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
    /// Fields that never travel in either direction: the subscription, the plan and the feature
    /// switches. On the server only the platform console sets them; on a PC its licence does. The
    /// slug is set once, when the PC first downloads its restaurant.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ServerOnlyFields = new Dictionary<string, string[]>
    {
        ["Restaurant"] = new[]
        {
            "Slug", "Plan", "PlanId", "PlanName", "Status", "CreatedAt", "PlanExpiresAt", "SetupFeePaid", "LastPaymentAt",
            "FeatureFbr", "FeatureOrdering", "FeatureKitchen", "FeatureWaiter", "FeatureBilling", "FeatureInventory", "FeatureReports",
        },
    };

    /// <summary>
    /// Counts the floor keeps up as it works: stock on hand and the last price paid, a member's points,
    /// and the moment a table's guest passes were renewed. Once the counter runs on the PC these come
    /// from the PC only. The server takes them from every PC change, even when its own copy of the rest
    /// of the row is newer, and the PC never takes them from the server.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> DeviceOwnedFields = new Dictionary<string, string[]>
    {
        ["StockItem"] = new[] { "Quantity", "LastCost" },
        ["LoyaltyMember"] = new[] { "Points", "LifetimePoints", "LifetimeSpend", "Visits" },
        ["DiningTable"] = new[] { "GuestPassesFrom" },
    };

    /// <summary>
    /// Secrets that never travel, in either direction: FBR tokens (each side keeps the one entered
    /// there; the PC files its own bills), a member's phone pass and a rider's link key. A change that
    /// arrives without them keeps this side's value.
    /// Sign-in hashes do travel, to the restaurant's own paired PC only, so staff can sign in there
    /// with no internet. They are salted PBKDF2, and a changed password still signs that person's
    /// other sessions out on both sides (the session stamp comes from the hash).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> SecretFields = new Dictionary<string, string[]>
    {
        ["StaffMember"] = new[] { "FbrToken" },
        ["Branch"] = new[] { "FbrToken" },
        ["LoyaltyMember"] = new[] { "Token" },
        ["TableSession"] = new[] { "RiderKey" },
    };

    public static bool IsSecret(string entityType, string field) =>
        SecretFields.TryGetValue(entityType, out var f) && f.Contains(field);

    public static bool IsServerOnly(string entityType, string field) =>
        ServerOnlyFields.TryGetValue(entityType, out var f) && f.Contains(field);

    public static bool IsDeviceOwned(string entityType, string field) =>
        DeviceOwnedFields.TryGetValue(entityType, out var f) && f.Contains(field);

    /// <summary>
    /// The number columns that point at another synced row. Their int values mean nothing on the other
    /// side, so they travel as the other row's SyncId and are turned back into this side's id on arrival.
    /// A test checks every "...Id" column of a synced type is listed here (or never travels).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> References =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["AttendanceMark"] = new Dictionary<string, string> { ["EmployeeId"] = "Employee" },
            ["CustomerEntry"] = new Dictionary<string, string> { ["CustomerId"] = "Customer", ["InvoiceId"] = "Invoice" },
            ["DayClosing"] = new Dictionary<string, string> { ["BranchId"] = "Branch" },
            ["DiningTable"] = new Dictionary<string, string> { ["BranchId"] = "Branch" },
            ["EmployeeEntry"] = new Dictionary<string, string> { ["EmployeeId"] = "Employee", ["ExpenseId"] = "Expense" },
            ["Expense"] = new Dictionary<string, string> { ["BranchId"] = "Branch" },
            ["Invoice"] = new Dictionary<string, string> { ["BranchId"] = "Branch", ["TableSessionId"] = "TableSession" },
            ["LoyaltyRedemption"] = new Dictionary<string, string> { ["MemberId"] = "LoyaltyMember", ["RewardId"] = "LoyaltyReward" },
            ["LoyaltyTransaction"] = new Dictionary<string, string>
            {
                ["InvoiceId"] = "Invoice", ["MemberId"] = "LoyaltyMember", ["RedemptionId"] = "LoyaltyRedemption", ["TableSessionId"] = "TableSession",
            },
            ["MenuItem"] = new Dictionary<string, string> { ["CategoryId"] = "MenuCategory" },
            ["Order"] = new Dictionary<string, string> { ["BranchId"] = "Branch", ["DiningTableId"] = "DiningTable", ["TableSessionId"] = "TableSession" },
            ["OrderItem"] = new Dictionary<string, string> { ["MenuItemId"] = "MenuItem" },
            ["OrderVoid"] = new Dictionary<string, string> { ["BranchId"] = "Branch", ["OrderId"] = "Order" },
            ["Payment"] = new Dictionary<string, string> { ["BranchId"] = "Branch", ["TableSessionId"] = "TableSession" },
            ["PaymentReturn"] = new Dictionary<string, string> { ["BranchId"] = "Branch", ["TableSessionId"] = "TableSession" },
            ["RecipeLine"] = new Dictionary<string, string> { ["StockItemId"] = "StockItem" },
            ["ServiceRequest"] = new Dictionary<string, string> { ["BranchId"] = "Branch" },
            ["StockMovement"] = new Dictionary<string, string> { ["StockItemId"] = "StockItem" },
            ["SupplierEntry"] = new Dictionary<string, string> { ["SupplierId"] = "Supplier", ["LinkedEntryId"] = "SupplierEntry" },
            ["SupplierEntryLine"] = new Dictionary<string, string> { ["StockItemId"] = "StockItem" },
            ["TableSession"] = new Dictionary<string, string>
            {
                ["BranchId"] = "Branch", ["CustomerId"] = "Customer", ["DiningTableId"] = "DiningTable", ["LoyaltyMemberId"] = "LoyaltyMember",
            },
        };

    /// <summary>The type a column points at, or null when it isn't a reference.</summary>
    public static string? ReferenceOf(string entityType, string field) =>
        References.TryGetValue(entityType, out var r) && r.TryGetValue(field, out var target) ? target : null;

    /// <summary>
    /// Floor and money records, and ledger lines. Once a restaurant's counter runs on its PC they are
    /// made on the PC only: the server refuses to make or change them, and takes the PC's copies.
    /// </summary>
    public static bool IsFloor(string entityType) =>
        Rules.TryGetValue(entityType, out var rule) && rule is SyncRule.OwnedByOrigin or SyncRule.Ledger;

    /// <summary>The floor types: what only the counter makes once a PC runs it (orders, bills, khata, stock movements…).</summary>
    public static readonly IReadOnlyList<string> FloorTypes =
        Rules.Where(kv => kv.Value is SyncRule.OwnedByOrigin or SyncRule.Ledger).Select(kv => kv.Key).ToArray();

    /// <summary>
    /// Who gives out bill numbers. Today only the server does. Once a restaurant pairs a PC that
    /// runs its counter, only that PC does, so offline bills can't repeat a number the server used.
    /// Not switched on in phase 1: nothing calls this yet.
    /// </summary>
    public static SyncSide BillNumbersFrom(bool restaurantHasActivePc) => restaurantHasActivePc ? SyncSide.Device : SyncSide.Server;
}
