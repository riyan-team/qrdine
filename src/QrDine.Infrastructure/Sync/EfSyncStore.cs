using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using QrDine.Application.Sync;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Infrastructure.Sync;

/// <summary>
/// Reads synced rows with their child rows, as plain field maps. Every read is held to one
/// restaurant twice over: by the tenant filter (the caller sets the device's restaurant) and by an
/// explicit restaurant check here. Secret fields (<see cref="SyncRules.SecretFields"/>) are left out.
/// </summary>
public sealed class EfSyncStore : ISyncStore
{
    private static readonly MethodInfo LoadOpen = typeof(EfSyncStore).GetMethod(nameof(LoadTyped), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo PageOpen = typeof(EfSyncStore).GetMethod(nameof(PageTyped), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly AppDbContext _db;
    private readonly Dictionary<string, IEntityType> _roots;

    public EfSyncStore(AppDbContext db)
    {
        _db = db;
        _roots = db.Model.GetEntityTypes().Where(SyncModel.IsRoot).ToDictionary(t => SyncModel.NameOf(t));
        RootTypes = _roots.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> RootTypes { get; }

    public async Task<IReadOnlyDictionary<Guid, SyncRow>?> Load(string type, int restaurantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (!_roots.TryGetValue(type, out var t)) return null;
        if (ids.Count == 0) return new Dictionary<Guid, SyncRow>();
        var task = (Task<List<object>>)LoadOpen.MakeGenericMethod(t.ClrType).Invoke(this, new object[] { t, restaurantId, ids.Distinct().ToList(), ct })!;
        var rows = (await task).Select(ToRow);
        return rows.ToDictionary(r => r.SyncId);
    }

    public async Task<IReadOnlyList<(int Id, SyncRow Row)>> Page(string type, int restaurantId, int afterId, int limit, CancellationToken ct = default)
    {
        if (!_roots.TryGetValue(type, out var t)) return Array.Empty<(int, SyncRow)>();
        var task = (Task<List<object>>)PageOpen.MakeGenericMethod(t.ClrType).Invoke(this, new object[] { t, restaurantId, afterId, limit, ct })!;
        return (await task).Select(e => ((int)_db.Entry(e).Property("Id").CurrentValue!, ToRow(e))).ToList();
    }

    private async Task<List<object>> LoadTyped<T>(IEntityType t, int restaurantId, List<Guid> ids, CancellationToken ct) where T : class
    {
        var rows = await Query<T>(t, restaurantId).Where(e => ids.Contains(EF.Property<Guid>(e, SyncModel.SyncIdProperty))).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    private async Task<List<object>> PageTyped<T>(IEntityType t, int restaurantId, int afterId, int limit, CancellationToken ct) where T : class
    {
        var rows = await Query<T>(t, restaurantId)
            .Where(e => EF.Property<int>(e, "Id") > afterId)
            .OrderBy(e => EF.Property<int>(e, "Id")).Take(limit).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    // Tracked on purpose: SyncId and LastChangedAt are shadow columns, read through the change tracker.
    private IQueryable<T> Query<T>(IEntityType t, int restaurantId) where T : class
    {
        IQueryable<T> q = _db.Set<T>();
        q = typeof(T) == typeof(Restaurant)
            ? q.Where(e => EF.Property<int>(e, "Id") == restaurantId)
            : q.Where(e => EF.Property<int>(e, "RestaurantId") == restaurantId);
        foreach (var path in SyncModel.IncludePaths(t)) q = q.Include(path);
        return q;
    }

    private SyncRow ToRow(object entity)
    {
        var e = _db.Entry(entity);
        return new SyncRow((Guid)e.Property(SyncModel.SyncIdProperty).CurrentValue!,
            (DateTime?)e.Property(SyncModel.LastChangedAtProperty).CurrentValue, Fields(e));
    }

    private Dictionary<string, object?> Fields(EntityEntry e)
    {
        var type = SyncModel.NameOf(e.Metadata);
        var fields = new Dictionary<string, object?>();
        foreach (var p in e.Properties)
        {
            if (SyncRules.IsSecret(type, p.Metadata.Name)) continue;
            fields[p.Metadata.Name] = p.CurrentValue is Enum en ? en.ToString() : p.CurrentValue;
        }
        foreach (var c in e.Collections)
        {
            var target = c.Metadata.TargetEntityType;
            if (!SyncModel.IsSynced(target) || SyncModel.IsRoot(target)) continue;
            var items = new List<Dictionary<string, object?>>();
            if (c.CurrentValue is System.Collections.IEnumerable children)
                foreach (var child in children) items.Add(Fields(_db.Entry(child)));
            fields[c.Metadata.Name] = items;
        }
        return fields;
    }
}
