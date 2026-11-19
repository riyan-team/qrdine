using QrDine.Domain.Common;

namespace QrDine.Domain.Sync;

/// <summary>What happened to a synced row.</summary>
public static class SyncOp
{
    public const string Upsert = "Upsert";   // created or changed: read its current state
    public const string Delete = "Delete";   // gone: the other side removes it too

    public static bool IsValid(string? op) => op is Upsert or Delete;
}

/// <summary>
/// A restaurant's own PC (or other device) that may sync that restaurant's data, and only that
/// restaurant's. The owner pairs it once; the device then signs in with its key. Only a hash of
/// the key is kept, so a database copy can't be used to sign in as the device.
/// </summary>
public class SyncDevice : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string KeyHash { get; set; } = "";          // SHA-256 of the device key, hex
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";
    public DateTime? LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null;
}

/// <summary>
/// One line of a restaurant's change list: "this row changed (or went) at this moment". A device
/// asks for the lines after the last one it has seen, then reads those rows' current state.
/// <see cref="Id"/> is the sequence number. Lines are written in the same save as the change itself,
/// so a change and its line are committed together or not at all.
/// </summary>
public class SyncChange : ITenantOwned
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public string EntityType { get; set; } = "";        // the synced root type, e.g. "Order"
    public Guid SyncId { get; set; }                     // the row's id everywhere
    public string Op { get; set; } = SyncOp.Upsert;
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// A change a device sent up, kept as it arrived. Phase 1 only stores it, once per
/// <see cref="ChangeId"/>; applying inbox lines to the restaurant's data (with <see cref="SyncRules"/>)
/// comes with the PC app.
/// </summary>
public class SyncInboxEntry : ITenantOwned
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public int DeviceId { get; set; }
    public Guid ChangeId { get; set; }                   // the device's id for this change: sending it again is a no-op
    public string EntityType { get; set; } = "";
    public Guid SyncId { get; set; }
    public string Op { get; set; } = SyncOp.Upsert;
    public string? Payload { get; set; }                 // the row as JSON, null for a delete
    public DateTime ClientChangedAt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = SyncInboxStatus.Pending;
}

public static class SyncInboxStatus
{
    public const string Pending = "Pending";
}
