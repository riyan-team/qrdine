using System.Text.Json;

namespace QrDine.Application.Sync;

/// <summary>One row as it is now: its sync id, when it last changed, and its fields (child rows included, secrets left out).</summary>
public sealed record SyncRow(Guid SyncId, DateTime? LastChangedAt, IReadOnlyDictionary<string, object?> Data);

/// <summary>Reads synced rows. Implemented over EF in Infrastructure; scoped to one restaurant by the caller.</summary>
public interface ISyncStore
{
    /// <summary>The root types a device can read, e.g. "Order", "MenuItem", "Restaurant".</summary>
    IReadOnlyList<string> RootTypes { get; }

    /// <summary>The current state of these rows. Rows that no longer exist are left out. Null for an unknown type.</summary>
    Task<IReadOnlyDictionary<Guid, SyncRow>?> Load(string type, int restaurantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Every row of a type, in id order, after <paramref name="afterId"/>: a new device's first download.</summary>
    Task<IReadOnlyList<(int Id, SyncRow Row)>> Page(string type, int restaurantId, int afterId, int limit, CancellationToken ct = default);
}

public sealed record PairedDevice(int Id, string Name, string Key);

public sealed record SyncDeviceRow(int Id, string Name, DateTime CreatedAt, string CreatedBy, DateTime? LastSeenAt, DateTime? RevokedAt);

public sealed record PairDeviceInput(string? Name);

public sealed record SyncHello(int RestaurantId, string Slug, string RestaurantName, int DeviceId, string DeviceName,
    DateTime ServerTime, long Head, IReadOnlyList<string> Types);

public sealed record SyncSnapshotPage(string Type, IReadOnlyList<SyncRow> Items, int? Next);

public sealed record SyncPulledChange(long Seq, string Type, Guid SyncId, string Op, DateTime ChangedAt,
    DateTime? LastChangedAt, IReadOnlyDictionary<string, object?>? Data);

public sealed record SyncPullPage(IReadOnlyList<SyncPulledChange> Changes, long Next, bool More);

/// <summary>A change a device sends up. <see cref="ChangeId"/> is the device's own id for it: sending it again is a no-op.</summary>
public sealed record SyncPushChange(Guid ChangeId, string? Type, Guid SyncId, string? Op, DateTime ChangedAt, JsonElement? Data);

public sealed record SyncPushInput(List<SyncPushChange>? Changes);

/// <summary><see cref="Busy"/>: another push with the same changes was saving at that moment; send the batch again.</summary>
public sealed record SyncPushResult(int Accepted, int AlreadyHad, bool Busy = false);
