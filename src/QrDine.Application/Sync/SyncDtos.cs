using System.Text.Json;

namespace QrDine.Application.Sync;

/// <summary>One row as it is now: its sync id, when it last changed, and its fields (child rows included, secrets left out).</summary>
public sealed record SyncRow(Guid SyncId, DateTime? LastChangedAt, IReadOnlyDictionary<string, object?> Data);

/// <summary>Reads synced rows. Implemented over EF in Infrastructure; scoped to one restaurant by the caller.</summary>
public interface ISyncStore
{
    /// <summary>The root types a device can read, e.g. "Order", "MenuItem", "Restaurant".</summary>
    IReadOnlyList<string> RootTypes { get; }

    /// <summary>The same types in the order a new copy is filled: each after the types it points at.</summary>
    IReadOnlyList<string> InDependencyOrder { get; }

    /// <summary>The current state of these rows. Rows that no longer exist are left out. Null for an unknown type.</summary>
    Task<IReadOnlyDictionary<Guid, SyncRow>?> Load(string type, int restaurantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Every row of a type, in id order, after <paramref name="afterId"/>: a new device's first download.</summary>
    Task<IReadOnlyList<(int Id, SyncRow Row)>> Page(string type, int restaurantId, int afterId, int limit, CancellationToken ct = default);
}

public sealed record PairedDevice(int Id, string Name, string Key);

public sealed record SyncDeviceRow(int Id, string Name, DateTime CreatedAt, string CreatedBy, DateTime? LastSeenAt, DateTime? RevokedAt,
    DateTime? CounterSince = null, int Waiting = 0, int Failed = 0, string? LastFailure = null);

public sealed record PairDeviceInput(string? Name);

/// <param name="Types">The synced types, in the order a new copy is filled.</param>
/// <param name="CounterSince">When this PC took over the counter; null until it has.</param>
public sealed record SyncHello(int RestaurantId, string Slug, string RestaurantName, int DeviceId, string DeviceName,
    DateTime ServerTime, long Head, IReadOnlyList<string> Types, DateTime? CounterSince = null, int MaxBatch = 500, int MaxPayloadChars = 0);

public sealed record SyncSnapshotPage(string Type, IReadOnlyList<SyncRow> Items, int? Next);

public sealed record SyncPulledChange(long Seq, string Type, Guid SyncId, string Op, DateTime ChangedAt,
    DateTime? LastChangedAt, IReadOnlyDictionary<string, object?>? Data);

public sealed record SyncPullPage(IReadOnlyList<SyncPulledChange> Changes, long Next, bool More);

/// <summary>A change a device sends up. <see cref="ChangeId"/> is the device's own id for it: sending it again is a no-op.</summary>
public sealed record SyncPushChange(Guid ChangeId, string? Type, Guid SyncId, string? Op, DateTime ChangedAt, JsonElement? Data);

public sealed record SyncPushInput(List<SyncPushChange>? Changes);

/// <summary>
/// <see cref="Busy"/>: another push with the same changes was saving at that moment; send the batch again.
/// <see cref="Applied"/>, <see cref="Skipped"/> and <see cref="Failed"/> count how the waiting changes went
/// when they were applied; <see cref="Notes"/> says why some failed.
/// </summary>
public sealed record SyncPushResult(int Accepted, int AlreadyHad, bool Busy = false, int Applied = 0, int Skipped = 0, int Failed = 0,
    IReadOnlyList<string>? Notes = null);

/// <summary>Which way a change is being applied.</summary>
public enum SyncApplySide
{
    /// <summary>On the server, from the restaurant's PC.</summary>
    OnServer,
    /// <summary>On the PC, from the server.</summary>
    OnPc,
}

/// <summary>A change from the other side, as it arrived. <see cref="Data"/> is the row (a JSON object), null for a delete.</summary>
public sealed record SyncIncoming(long Seq, string Type, Guid SyncId, string Op, DateTime? ChangedAt, JsonElement? Data);

/// <summary>What became of one incoming change: <c>Applied</c>, <c>Skipped</c> or <c>Failed</c>, with why.</summary>
public sealed record SyncApplied(long Seq, string Type, Guid SyncId, string Status, string? Note);

/// <param name="Side">Where this runs.</param>
/// <param name="Origin">On the server the device's id; on a PC <see cref="QrDine.Domain.Sync.SyncChange.FromServer"/>.</param>
/// <param name="TakeEverything">A PC filling itself from the server (first download, and catching up until it
/// takes over the counter): it takes the server's floor records and counts too.</param>
public sealed record SyncApplyOptions(SyncApplySide Side, int Origin, bool TakeEverything = false);

/// <summary>Applies the other side's changes to this side's data with the sync rules (Infrastructure/Sync/SyncApplier).</summary>
public interface ISyncApplier
{
    Task<IReadOnlyList<SyncApplied>> Apply(int restaurantId, IReadOnlyList<SyncIncoming> changes, SyncApplyOptions options, CancellationToken ct = default);
}
