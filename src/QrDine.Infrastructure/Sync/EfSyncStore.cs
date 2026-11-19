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
/// Reads synced rows with their child rows, as plain field maps ready to send to the other side.
/// Every read is held to one restaurant twice over: by the tenant filter (the caller sets the
/// restaurant) and by an explicit restaurant check here.
/// <para>What travels: every field except this side's own int id, the restaurant id (each side has its
/// own), a child's link to its parent (it travels inside the parent), the secrets
/// (<see cref="SyncRules.SecretFields"/>) and the fields that never travel
/// (<see cref="SyncRules.ServerOnlyFields"/>). A column that points at another row
/// (<see cref="SyncRules.References"/>) travels as that row's SyncId. A child row carries its own SyncId.</para>
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
        InDependencyOrder = SyncKeys.DependencyOrder(RootTypes, RootOf);
    }

    public IReadOnlyList<string> RootTypes { get; }

    public IReadOnlyList<string> InDependencyOrder { get; }

    private string RootOf(string type)
    {
        var t = _db.Model.GetEntityTypes().FirstOrDefault(x => SyncModel.NameOf(x) == type);
        return t is null ? type : SyncModel.RootTypeName(t);
    }

    public async Task<IReadOnlyDictionary<Guid, SyncRow>?> Load(string type, int restaurantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (!_roots.TryGetValue(type, out var t)) return null;
        if (ids.Count == 0) return new Dictionary<Guid, SyncRow>();
        var task = (Task<List<object>>)LoadOpen.MakeGenericMethod(t.ClrType).Invoke(this, new object[] { t, restaurantId, ids.Distinct().ToList(), ct })!;
        var rows = await ToRows(await task, ct);
        return rows.ToDictionary(r => r.SyncId);
    }

    public async Task<IReadOnlyList<(int Id, SyncRow Row)>> Page(string type, int restaurantId, int afterId, int limit, CancellationToken ct = default)
    {
        if (!_roots.TryGetValue(type, out var t)) return Array.Empty<(int, SyncRow)>();
        var task = (Task<List<object>>)PageOpen.MakeGenericMethod(t.ClrType).Invoke(this, new object[] { t, restaurantId, afterId, limit, ct })!;
        var entities = await task;
        var rows = await ToRows(entities, ct);
        return entities.Select((e, i) => ((int)_db.Entry(e).Property("Id").CurrentValue!, rows[i])).ToList();
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
        return q.AsSplitQuery();
    }

    private async Task<List<SyncRow>> ToRows(List<object> entities, CancellationToken ct)
    {
        var keys = new SyncKeys(_db);
        foreach (var e in entities) WantRefs(_db.Entry(e), keys);
        await keys.ResolveSyncIds(ct);
        return entities.Select(e =>
        {
            var entry = _db.Entry(e);
            return new SyncRow((Guid)entry.Property(SyncModel.SyncIdProperty).CurrentValue!,
                (DateTime?)entry.Property(SyncModel.LastChangedAtProperty).CurrentValue, Fields(entry, keys, child: false));
        }).ToList();
    }

    private void WantRefs(EntityEntry e, SyncKeys keys)
    {
        var type = SyncModel.NameOf(e.Metadata);
        if (SyncRules.References.TryGetValue(type, out var refs))
            foreach (var (field, target) in refs)
                if (e.Property(field).CurrentValue is int id) keys.WantSyncId(target, id);
        foreach (var c in e.Collections)
        {
            if (!SyncModel.IsSynced(c.Metadata.TargetEntityType) || SyncModel.IsRoot(c.Metadata.TargetEntityType)) continue;
            if (c.CurrentValue is System.Collections.IEnumerable children)
                foreach (var child in children) WantRefs(_db.Entry(child), keys);
        }
    }

    private Dictionary<string, object?> Fields(EntityEntry e, SyncKeys keys, bool child)
    {
        var type = SyncModel.NameOf(e.Metadata);
        var parentKey = child ? SyncModel.ParentKey(e.Metadata).Properties[0].Name : null;
        var fields = new Dictionary<string, object?>();
        foreach (var p in e.Properties)
        {
            var name = p.Metadata.Name;
            if (p.Metadata.IsPrimaryKey() || name == SyncModel.LastChangedAtProperty || name == parentKey) continue;
            if (name == SyncModel.SyncIdProperty) { if (child) fields[name] = p.CurrentValue; continue; }
            if (name == "RestaurantId" && type != nameof(Restaurant)) continue;
            if (SyncRules.IsSecret(type, name) || SyncRules.IsServerOnly(type, name)) continue;
            if (SyncRules.ReferenceOf(type, name) is string target)
            {
                fields[name] = keys.SyncIdOf(target, p.CurrentValue as int?);
                continue;
            }
            fields[name] = p.CurrentValue is Enum en ? en.ToString() : p.CurrentValue;
        }
        foreach (var c in e.Collections)
        {
            var target = c.Metadata.TargetEntityType;
            if (!SyncModel.IsSynced(target) || SyncModel.IsRoot(target)) continue;
            var items = new List<Dictionary<string, object?>>();
            if (c.CurrentValue is System.Collections.IEnumerable children)
                foreach (var ch in children) items.Add(Fields(_db.Entry(ch), keys, child: true));
            fields[c.Metadata.Name] = items;
        }
        return fields;
    }
}
