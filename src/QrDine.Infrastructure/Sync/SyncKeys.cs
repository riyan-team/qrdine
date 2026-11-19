using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Infrastructure.Sync;

/// <summary>
/// Turns this side's int ids into SyncIds and back, a batch at a time, within the current restaurant
/// (the tenant filter is on). The int ids differ between the server and a PC; the SyncIds are the same.
/// </summary>
public sealed class SyncKeys
{
    private static readonly MethodInfo ToSyncOpen = typeof(SyncKeys).GetMethod(nameof(ToSyncTyped), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ToLocalOpen = typeof(SyncKeys).GetMethod(nameof(ToLocalTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly AppDbContext _db;
    private readonly Dictionary<string, IEntityType> _roots;
    private readonly Dictionary<(string Type, int Id), Guid> _toSync = new();
    private readonly Dictionary<(string Type, Guid Id), int> _toLocal = new();
    private readonly Dictionary<string, HashSet<int>> _wantSync = new();
    private readonly Dictionary<string, HashSet<Guid>> _wantLocal = new();

    public SyncKeys(AppDbContext db)
    {
        _db = db;
        _roots = db.Model.GetEntityTypes().Where(SyncModel.IsRoot).ToDictionary(t => SyncModel.NameOf(t));
    }

    public IEntityType? Root(string type) => _roots.GetValueOrDefault(type);

    // ---- this side's id -> SyncId (sending) ----

    public void WantSyncId(string type, int id)
    {
        if (id <= 0 || type == nameof(Restaurant) || _toSync.ContainsKey((type, id))) return;
        (_wantSync.TryGetValue(type, out var set) ? set : _wantSync[type] = new()).Add(id);
    }

    public async Task ResolveSyncIds(CancellationToken ct)
    {
        foreach (var (type, ids) in _wantSync)
        {
            if (!_roots.TryGetValue(type, out var t) || ids.Count == 0) continue;
            var task = (Task<List<(int, Guid)>>)ToSyncOpen.MakeGenericMethod(t.ClrType).Invoke(null, new object[] { _db, ids.ToList(), ct })!;
            foreach (var (id, sid) in await task) _toSync[(type, id)] = sid;
        }
        _wantSync.Clear();
    }

    public Guid? SyncIdOf(string type, int? id) =>
        id is int v && v > 0 && _toSync.TryGetValue((type, v), out var g) ? g : null;

    // ---- SyncId -> this side's id (receiving) ----

    public void WantLocalId(string type, Guid id)
    {
        if (id == Guid.Empty || _toLocal.ContainsKey((type, id))) return;
        (_wantLocal.TryGetValue(type, out var set) ? set : _wantLocal[type] = new()).Add(id);
    }

    public async Task ResolveLocalIds(CancellationToken ct)
    {
        foreach (var (type, ids) in _wantLocal)
        {
            if (!_roots.TryGetValue(type, out var t) || ids.Count == 0) continue;
            var task = (Task<List<(Guid, int)>>)ToLocalOpen.MakeGenericMethod(t.ClrType).Invoke(null, new object[] { _db, ids.ToList(), ct })!;
            foreach (var (sid, id) in await task) _toLocal[(type, sid)] = id;
        }
        _wantLocal.Clear();
    }

    public int? LocalIdOf(string type, Guid id) => _toLocal.TryGetValue((type, id), out var v) ? v : null;

    /// <summary>A row this side just saved: its id is known now.</summary>
    public void Learn(string type, Guid syncId, int id)
    {
        _toLocal[(type, syncId)] = id;
        _toSync[(type, id)] = syncId;
    }

    /// <summary>A row this side just deleted.</summary>
    public void Forget(string type, Guid syncId) => _toLocal.Remove((type, syncId));

    private static async Task<List<(int, Guid)>> ToSyncTyped<T>(AppDbContext db, List<int> ids, CancellationToken ct) where T : class
    {
        var rows = await db.Set<T>().AsNoTracking()
            .Where(e => ids.Contains(EF.Property<int>(e, "Id")))
            .Select(e => new { Id = EF.Property<int>(e, "Id"), Sid = EF.Property<Guid>(e, SyncModel.SyncIdProperty) })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Sid)).ToList();
    }

    private static async Task<List<(Guid, int)>> ToLocalTyped<T>(AppDbContext db, List<Guid> ids, CancellationToken ct) where T : class
    {
        var rows = await db.Set<T>().AsNoTracking()
            .Where(e => ids.Contains(EF.Property<Guid>(e, SyncModel.SyncIdProperty)))
            .Select(e => new { Id = EF.Property<int>(e, "Id"), Sid = EF.Property<Guid>(e, SyncModel.SyncIdProperty) })
            .ToListAsync(ct);
        return rows.Select(r => (r.Sid, r.Id)).ToList();
    }

    /// <summary>
    /// Root types in the order a new copy must be filled: a type comes after every type it points at
    /// (a menu item after its category, an order after its table and items), so a row's references
    /// already exist when it arrives. Child rows count for their root.
    /// </summary>
    public static IReadOnlyList<string> DependencyOrder(IEnumerable<string> rootTypes, Func<string, string> rootOf)
    {
        var roots = rootTypes.ToHashSet();
        var needs = roots.ToDictionary(t => t, _ => new HashSet<string>());
        foreach (var (type, refs) in SyncRules.References)
        {
            var owner = rootOf(type);
            if (!needs.ContainsKey(owner)) continue;
            foreach (var target in refs.Values)
                if (target != owner && roots.Contains(target)) needs[owner].Add(target);
        }
        var order = new List<string>();
        var done = new HashSet<string>();
        // The restaurant row first, then the rest by name within each level, so the order is stable.
        void Visit(string t, HashSet<string> path)
        {
            if (done.Contains(t)) return;
            if (!path.Add(t)) throw new InvalidOperationException($"Sync references loop through {t}.");
            foreach (var n in needs[t].OrderBy(x => x, StringComparer.Ordinal)) Visit(n, path);
            path.Remove(t);
            done.Add(t);
            order.Add(t);
        }
        if (roots.Contains(nameof(Restaurant))) Visit(nameof(Restaurant), new());
        foreach (var t in roots.OrderBy(x => x, StringComparer.Ordinal)) Visit(t, new());
        return order;
    }
}
