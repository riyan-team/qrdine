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

    /// <summary>
    /// When this PC took over the restaurant's counter (after its first download). From then on the
    /// server makes no orders, bills or other floor records for the restaurant: the PC does.
    /// </summary>
    public DateTime? CounterSince { get; set; }

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

    /// <summary>
    /// Where the change came from when it was applied from the other side: on the server, the device
    /// that sent it (so that device isn't sent its own change back); on a PC, <see cref="FromServer"/>
    /// (so the PC doesn't send the server's change back up). Null for a change made here.
    /// </summary>
    public int? OriginDeviceId { get; set; }

    public const int FromServer = -1;
}

/// <summary>
/// A change a device sent up, kept as it arrived, once per <see cref="ChangeId"/>. It is applied to the
/// restaurant's data with <see cref="SyncRules"/> straight after it is stored; <see cref="Status"/> and
/// <see cref="Note"/> say how that went.
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
    public DateTime? AppliedAt { get; set; }
    public int Attempts { get; set; }
    public string? Note { get; set; }                    // why it was skipped or failed, for the Devices screen
}

public static class SyncInboxStatus
{
    public const string Pending = "Pending";   // stored, not applied yet
    public const string Applied = "Applied";   // the restaurant's data now has it
    public const string Skipped = "Skipped";   // this side's copy was newer, or it changed nothing
    public const string Failed = "Failed";     // could not be applied; Note says why
}
