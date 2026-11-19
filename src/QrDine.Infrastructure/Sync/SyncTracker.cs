using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;

namespace QrDine.Infrastructure.Sync;

/// <summary>
/// Runs just before every save. It gives new synced rows their SyncId, stamps LastChangedAt on
/// what changed (a changed child stamps its root too), and, when sync is switched on, adds one
/// change-list line per changed root. The lines go into the same save, so they commit with the
/// change or not at all. It never changes a value the app itself set, so what the app saves and
/// answers stays exactly the same.
/// </summary>
public static class SyncTracker
{
    /// <param name="applyingFrom">Set when this save applies a change from the other side: rows keep the
    /// change times the applier gave them, and the lines carry the origin (see <see cref="SyncChange.OriginDeviceId"/>).</param>
    public static List<SyncChange> Prepare(DbContext db, bool recordChanges, DateTime now, int? applyingFrom = null)
    {
        var changed = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && SyncModel.IsSynced(e.Metadata))
            .ToList();
        var lines = new List<SyncChange>();
        if (changed.Count == 0) return lines;

        foreach (var e in changed)
        {
            if (e.State == EntityState.Added)
            {
                var id = e.Property(SyncModel.SyncIdProperty);
                if (id.CurrentValue is not Guid g || g == Guid.Empty) id.CurrentValue = Guid.NewGuid();
            }
            if (e.State == EntityState.Deleted) continue;
            var changedAt = e.Property(SyncModel.LastChangedAtProperty);
            if (applyingFrom is null || changedAt.CurrentValue is null) changedAt.CurrentValue = now;
        }

        var tracked = new TrackedRows(db);
        var byRoot = new Dictionary<(string Type, Guid Id), SyncChange>();
        foreach (var e in changed)
        {
            var root = RootOf(db, e, tracked);
            if (root is null) continue;
            // A child changed: its root changed too (an order whose items changed is a changed order).
            if (!ReferenceEquals(root, e) && root.State is EntityState.Unchanged or EntityState.Modified && applyingFrom is null)
                root.Property(SyncModel.LastChangedAtProperty).CurrentValue = now;
            if (!recordChanges) continue;

            var deleted = root.State == EntityState.Deleted;
            var rid = RestaurantIdOf(root, deleted);
            var syncId = Value(root.Property(SyncModel.SyncIdProperty), deleted) as Guid? ?? Guid.Empty;
            if (rid <= 0 || syncId == Guid.Empty) continue;   // e.g. a restaurant being signed up: a new PC starts from a snapshot

            var key = (SyncModel.NameOf(root.Metadata), syncId);
            if (byRoot.TryGetValue(key, out var line))
            {
                if (deleted) line.Op = SyncOp.Delete;
                continue;
            }
            byRoot[key] = new SyncChange
            {
                RestaurantId = rid, EntityType = key.Item1, SyncId = syncId,
                Op = deleted ? SyncOp.Delete : SyncOp.Upsert, ChangedAt = now, OriginDeviceId = applyingFrom,
            };
        }

        if (byRoot.Count > 0)
        {
            lines.AddRange(byRoot.Values);
            db.Set<SyncChange>().AddRange(lines);
        }
        return lines;
    }

    /// <summary>
    /// A save failed: take its change-list lines back out. Bill and ticket numbering retry a failed
    /// save on the same rows, and the retry adds its own lines.
    /// </summary>
    public static void Forget(DbContext db, List<SyncChange> lines)
    {
        foreach (var line in lines)
        {
            var e = db.Entry(line);
            if (e.State == EntityState.Added) e.State = EntityState.Detached;
        }
    }

    private static object? Value(PropertyEntry p, bool deleted) => deleted ? p.OriginalValue : p.CurrentValue;

    private static int RestaurantIdOf(EntityEntry root, bool deleted)
    {
        var name = root.Metadata.ClrType == typeof(Restaurant) ? "Id" : "RestaurantId";
        return Value(root.Property(name), deleted) as int? ?? 0;
    }

    /// <summary>The root a row belongs to: itself for a root, otherwise up through its parents.</summary>
    private static EntityEntry? RootOf(DbContext db, EntityEntry e, TrackedRows tracked)
    {
        var current = e;
        for (var depth = 0; !SyncModel.IsRoot(current.Metadata); depth++)
        {
            if (depth > 8) return null;
            var fk = SyncModel.ParentKey(current.Metadata);
            if (fk.Properties.Count != 1) return null;
            var deleted = current.State == EntityState.Deleted;
            var parentKey = Value(current.Property(fk.Properties[0].Name), deleted);
            if (parentKey is null) return null;
            var parent = tracked.Find(fk.PrincipalEntityType, parentKey);
            if (parent is null)
            {
                // Rare: a child saved without its parent loaded. Load the parent to find its root.
                var found = db.Find(fk.PrincipalEntityType.ClrType, parentKey);
                if (found is null) return null;
                parent = db.Entry(found);
            }
            current = parent;
        }
        return current;
    }

    /// <summary>Tracked synced rows by type and key. New rows are found by their temporary key, which their children share.</summary>
    private sealed class TrackedRows
    {
        private readonly Dictionary<(IReadOnlyEntityType, object), EntityEntry> _rows = new();

        public TrackedRows(DbContext db)
        {
            foreach (var e in db.ChangeTracker.Entries())
            {
                if (!SyncModel.IsSynced(e.Metadata)) continue;
                var pk = e.Metadata.FindPrimaryKey();
                if (pk is not { Properties.Count: 1 }) continue;
                var p = e.Property(pk.Properties[0].Name);
                var key = e.State == EntityState.Deleted ? p.OriginalValue : p.CurrentValue;
                if (key is not null) _rows[(e.Metadata, key)] = e;
            }
        }

        public EntityEntry? Find(IReadOnlyEntityType type, object key) => _rows.GetValueOrDefault((type, key));
    }
}
