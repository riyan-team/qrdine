using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using QrDine.Application.Sync;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Infrastructure.Sync;

/// <summary>
/// Applies changes from the other side (the server's on a PC, the PC's on the server) to this side's
/// data, with <see cref="SyncRules"/>:
/// <list type="bullet">
/// <item><b>Settings</b> (menu, tables, staff…): the newest change wins. On the server a PC's counts
/// (<see cref="SyncRules.DeviceOwnedFields"/>) are always taken; on the PC they are never taken from the server.</item>
/// <item><b>Floor and money records</b>: made on the PC. The server takes the PC's copy; the PC keeps its own
/// (except while it first fills itself from the server).</item>
/// <item>Secrets and the fields that never travel are never set from the other side.</item>
/// </list>
/// Rows go in dependency order, so a row's references exist when it arrives; a row whose reference
/// hasn't arrived waits for the rest of the batch, and if it never comes the link is left empty (and
/// noted) rather than stopping everything. Each type's rows are saved together; if that save fails, they
/// are saved one by one so one bad row fails alone. Runs in its own scope, held to the one restaurant.
/// </summary>
public sealed class SyncApplier : ISyncApplier
{
    private static readonly MethodInfo FindOpen = typeof(SyncApplier).GetMethod(nameof(FindTyped), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
    private static IReadOnlyList<string>? _order;

    private readonly IServiceScopeFactory _scopes;

    public SyncApplier(IServiceScopeFactory scopes) => _scopes = scopes;

    private sealed class NeedRef(string type, Guid id) : Exception($"waits for {type} {id}");

    private enum Mode { Skip, All, AllButCounts, CountsOnly }

    // A staged change: the row it touches and how, ready to save.
    private sealed record Staged(SyncIncoming Change, Func<EntityEntry?> Run, string? Note);

    public async Task<IReadOnlyList<SyncApplied>> Apply(int restaurantId, IReadOnlyList<SyncIncoming> changes, SyncApplyOptions options, CancellationToken ct = default)
    {
        var results = new Dictionary<SyncIncoming, SyncApplied>(ReferenceEqualityComparer.Instance);
        if (changes.Count == 0) return Array.Empty<SyncApplied>();

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var restaurant = await db.Restaurants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == restaurantId, ct)
            ?? throw new InvalidOperationException($"Restaurant {restaurantId} isn't here.");
        scope.ServiceProvider.GetRequiredService<CurrentTenant>().Set(restaurant.Id, restaurant.Slug);
        db.ApplyingFrom = options.Origin;
        var keys = new SyncKeys(db);
        var order = _order ??= new EfSyncStore(db).InDependencyOrder;
        var rank = order.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);

        var pending = changes.OrderBy(c => rank.GetValueOrDefault(c.Type, int.MaxValue)).ThenBy(c => c.Seq).ToList();
        var dangling = false;
        while (pending.Count > 0)
        {
            var waiting = new List<SyncIncoming>();
            foreach (var group in Runs(pending))
            {
                foreach (var c in group) WantRefs(keys, c);
                await keys.ResolveLocalIds(ct);

                var staged = new List<Staged>();
                foreach (var c in group)
                    if (await TryStage(db, keys, restaurant, c, options, dangling, results, waiting, ct) is { } s) staged.Add(s);
                if (staged.Count == 0) { db.ChangeTracker.Clear(); continue; }

                // All of this type together. If that fails, one at a time, each read afresh, so a bad row fails alone.
                if (await TrySave(db, keys, staged, results, alone: false, ct)) continue;
                foreach (var s in staged)
                    if (await TryStage(db, keys, restaurant, s.Change, options, dangling, results, waiting, ct) is { } again)
                        await TrySave(db, keys, new List<Staged> { again }, results, alone: true, ct);
            }

            if (waiting.Count == 0) break;
            if (waiting.Count == pending.Count)
            {
                if (dangling)
                {
                    foreach (var c in waiting) results[c] = new(c.Seq, c.Type, c.SyncId, SyncInboxStatus.Failed, "A row it points at never arrived.");
                    break;
                }
                dangling = true;   // no progress: put them in with the missing links left empty (and noted)
            }
            pending = waiting;
        }
        return changes.Select(c => results.TryGetValue(c, out var r) ? r : new SyncApplied(c.Seq, c.Type, c.SyncId, SyncInboxStatus.Failed, "Not applied.")).ToList();
    }

    private static async Task<Staged?> TryStage(AppDbContext db, SyncKeys keys, Restaurant restaurant, SyncIncoming c, SyncApplyOptions o, bool dangling,
        Dictionary<SyncIncoming, SyncApplied> results, List<SyncIncoming> waiting, CancellationToken ct)
    {
        try { return await Stage(db, keys, restaurant, c, o, dangling, ct); }
        catch (NeedRef) { waiting.Add(c); }
        catch (SkipException skip) { results[c] = new(c.Seq, c.Type, c.SyncId, SyncInboxStatus.Skipped, skip.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { results[c] = new(c.Seq, c.Type, c.SyncId, SyncInboxStatus.Failed, Short(ex)); }
        return null;
    }

    private static IEnumerable<List<SyncIncoming>> Runs(List<SyncIncoming> ordered)
    {
        var run = new List<SyncIncoming>();
        foreach (var c in ordered)
        {
            if (run.Count > 0 && (run[0].Type != c.Type || run.Count >= 200)) { yield return run; run = new(); }
            run.Add(c);
        }
        if (run.Count > 0) yield return run;
    }

    private static async Task<bool> TrySave(AppDbContext db, SyncKeys keys, List<Staged> staged, Dictionary<SyncIncoming, SyncApplied> results,
        bool alone, CancellationToken ct)
    {
        try
        {
            var done = staged.Select(s => { var e = s.Run(); return (S: s, E: e, Deleted: e?.State == EntityState.Deleted); }).ToList();
            await db.SaveChangesAsync(ct);
            foreach (var (s, e, deleted) in done)
            {
                if (deleted) keys.Forget(s.Change.Type, s.Change.SyncId);
                else if (e is not null) keys.Learn(s.Change.Type, s.Change.SyncId, (int)e.Property("Id").CurrentValue!);
                results[s.Change] = new(s.Change.Seq, s.Change.Type, s.Change.SyncId, SyncInboxStatus.Applied, s.Note);
            }
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (alone) results[staged[0].Change] = new(staged[0].Change.Seq, staged[0].Change.Type, staged[0].Change.SyncId, SyncInboxStatus.Failed, Short(ex));
            return false;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private sealed class SkipException(string why) : Exception(why);

    private static string Short(Exception ex)
    {
        var m = (ex.InnerException ?? ex).Message;
        return m.Length > 380 ? m[..380] : m;
    }

    private static void WantRefs(SyncKeys keys, SyncIncoming c)
    {
        if (c.Data is not { ValueKind: JsonValueKind.Object } data || keys.Root(c.Type) is not { } et) return;
        WantRefs(keys, et, data);
    }

    // The references in a row and in its child rows (an order's items point at menu items), from the model.
    private static void WantRefs(SyncKeys keys, IEntityType et, JsonElement data)
    {
        if (SyncRules.References.TryGetValue(SyncModel.NameOf(et), out var refs))
            foreach (var (field, target) in refs)
                if (data.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var g))
                    keys.WantLocalId(target, g);
        foreach (var nav in et.GetNavigations())
        {
            if (!nav.IsCollection || !SyncModel.IsSynced(nav.TargetEntityType) || SyncModel.IsRoot(nav.TargetEntityType)) continue;
            if (!data.TryGetProperty(nav.Name, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var child in arr.EnumerateArray())
                if (child.ValueKind == JsonValueKind.Object) WantRefs(keys, nav.TargetEntityType, child);
        }
    }

    /// <summary>Works out what an incoming change does, without touching anything yet. Null when it changes nothing.</summary>
    private static async Task<Staged?> Stage(AppDbContext db, SyncKeys keys, Restaurant restaurant, SyncIncoming c, SyncApplyOptions o, bool dangling, CancellationToken ct)
    {
        var et = keys.Root(c.Type) ?? throw new InvalidOperationException($"Unknown type {c.Type}.");
        var existing = await Find(db, et, restaurant.Id, c.SyncId, ct);
        var floor = SyncRules.IsFloor(c.Type);
        var existingAt = existing is null ? null : (DateTime?)existing.Property(SyncModel.LastChangedAtProperty).CurrentValue;
        var newer = existingAt is null || (c.ChangedAt is DateTime a && a > existingAt);
        var notOlder = existingAt is null || (c.ChangedAt is DateTime b && b >= existingAt);

        if (c.Op == SyncOp.Delete)
        {
            if (existing is null) throw new SkipException("Not here.");
            if (c.Type == nameof(Restaurant)) throw new SkipException("A restaurant is never removed by sync.");
            if (o.Side == SyncApplySide.OnPc && floor && !o.TakeEverything) throw new SkipException("The PC keeps its own floor records.");
            if (!floor && !notOlder) throw new SkipException("Changed here after it was removed there.");
            return new Staged(c, () => { db.Remove(existing.Entity); return db.Entry(existing.Entity); }, null);
        }

        if (c.Data is not { ValueKind: JsonValueKind.Object } data) throw new InvalidOperationException("An upsert needs the row.");
        Mode mode;
        if (existing is null)
        {
            if (c.Type == nameof(Restaurant)) throw new InvalidOperationException("This isn't this restaurant's row.");
            mode = Mode.All;
        }
        else if (o.Side == SyncApplySide.OnPc && o.TakeEverything)
            mode = Mode.All;   // the download: nothing is made here yet, so the server's copy is the truth (the PC's own blank restaurant row included)
        else if (o.Side == SyncApplySide.OnServer)
            mode = floor ? (notOlder ? Mode.All : Mode.Skip) : newer ? Mode.All : Mode.CountsOnly;
        else
            mode = floor ? (o.TakeEverything && newer ? Mode.All : Mode.Skip)
                : newer ? (o.TakeEverything ? Mode.All : Mode.AllButCounts) : Mode.Skip;
        if (mode == Mode.Skip)
            throw new SkipException(floor && o.Side == SyncApplySide.OnPc ? "The PC keeps its own floor records." : "This side's copy is newer.");

        var notes = new List<string>();
        var sets = Plan(keys, et, c.Type, data, existing, mode, dangling, notes, child: false);
        var children = mode == Mode.CountsOnly ? null : PlanChildren(keys, et, data, existing, dangling, notes);
        if (mode == Mode.CountsOnly && sets.All(s => Equals(existing!.Property(s.Name).CurrentValue, s.Value)))
            throw new SkipException("This side's copy is newer.");
        var note = notes.Count == 0 ? null : string.Join(" ", notes.Distinct());

        return new Staged(c, () =>
        {
            EntityEntry entry;
            if (existing is null)
            {
                var row = Activator.CreateInstance(et.ClrType, nonPublic: true)!;
                entry = db.Add(row);
                entry.Property(SyncModel.SyncIdProperty).CurrentValue = c.SyncId;
                if (et.FindProperty("RestaurantId") is not null) entry.Property("RestaurantId").CurrentValue = restaurant.Id;
            }
            else entry = existing;
            foreach (var (name, value) in sets) entry.Property(name).CurrentValue = value;
            if (mode != Mode.CountsOnly) entry.Property(SyncModel.LastChangedAtProperty).CurrentValue = c.ChangedAt ?? DateTime.UtcNow;
            children?.Invoke(db, entry, c.ChangedAt ?? DateTime.UtcNow);
            return entry;
        }, note);
    }

    /// <summary>The field values a change sets on a row (not children).</summary>
    private static List<(string Name, object? Value)> Plan(SyncKeys keys, IEntityType et, string type, JsonElement data, EntityEntry? existing,
        Mode mode, bool dangling, List<string> notes, bool child)
    {
        var parentKey = child ? SyncModel.ParentKey(et).Properties[0].Name : null;
        var sets = new List<(string, object?)>();
        foreach (var p in et.GetProperties())
        {
            var name = p.Name;
            if (p.IsPrimaryKey() || name == SyncModel.SyncIdProperty || name == SyncModel.LastChangedAtProperty || name == parentKey) continue;
            if (name == "RestaurantId" && type != nameof(Restaurant)) continue;
            if (SyncRules.IsSecret(type, name) || SyncRules.IsServerOnly(type, name)) continue;
            var counts = SyncRules.IsDeviceOwned(type, name);
            if (mode == Mode.CountsOnly && !counts) continue;
            if (mode == Mode.AllButCounts && counts && existing is not null) continue;
            if (!data.TryGetProperty(name, out var v)) continue;   // an older build that doesn't send this field
            sets.Add((name, SyncRules.ReferenceOf(type, name) is string target
                ? Resolve(keys, target, v, p.ClrType, dangling, notes)
                : Convert(v, p.ClrType)));
        }
        return sets;
    }

    private static object? Resolve(SyncKeys keys, string target, JsonElement v, Type clr, bool dangling, List<string> notes)
    {
        var nullable = Nullable.GetUnderlyingType(clr) is not null;
        if (v.ValueKind != JsonValueKind.String || !v.TryGetGuid(out var g)) return nullable ? null : 0;
        if (keys.LocalIdOf(target, g) is int id) return id;
        if (!dangling) throw new NeedRef(target, g);
        notes.Add($"Its {target} wasn't there, so that link is empty.");
        return nullable ? null : 0;
    }

    private static object? Convert(JsonElement v, Type clr)
    {
        var under = Nullable.GetUnderlyingType(clr);
        if (v.ValueKind == JsonValueKind.Null)
            return under is not null || !clr.IsValueType ? null : Activator.CreateInstance(clr);
        var t = under ?? clr;
        if (t.IsEnum)
            return v.ValueKind == JsonValueKind.Number ? Enum.ToObject(t, v.GetInt64()) : Enum.Parse(t, v.GetString()!, ignoreCase: true);
        return JsonSerializer.Deserialize(v.GetRawText(), clr, Json);
    }

    /// <summary>
    /// The child rows (an order's items and their modifiers, a bill's lines…) as the other side has them:
    /// matched by their SyncId, changed, added, and the ones it no longer has removed.
    /// </summary>
    private static Action<AppDbContext, EntityEntry, DateTime>? PlanChildren(SyncKeys keys, IEntityType et, JsonElement data, EntityEntry? existing,
        bool dangling, List<string> notes)
    {
        var steps = new List<Action<AppDbContext, EntityEntry, DateTime>>();
        foreach (var nav in et.GetNavigations())
        {
            if (!nav.IsCollection || nav.ForeignKey.DeleteBehavior != DeleteBehavior.Cascade) continue;
            var ct = nav.TargetEntityType;
            if (!SyncModel.IsSynced(ct) || SyncModel.IsRoot(ct)) continue;
            if (!data.TryGetProperty(nav.Name, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;

            var typeName = SyncModel.NameOf(ct);
            var current = new Dictionary<Guid, object>();
            if (existing?.Collection(nav.Name).CurrentValue is IEnumerable have)
                foreach (var ch in have)
                    if (existing.Context.Entry(ch).Property(SyncModel.SyncIdProperty).CurrentValue is Guid g) current[g] = ch;

            var plans = new List<(Guid Id, object? Have, List<(string, object?)> Sets, Action<AppDbContext, EntityEntry, DateTime>? Deeper)>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = item.TryGetProperty(SyncModel.SyncIdProperty, out var sid) && sid.TryGetGuid(out var gg) ? gg : Guid.NewGuid();
                current.TryGetValue(id, out var have2);
                var haveEntry = have2 is null ? null : existing!.Context.Entry(have2);
                plans.Add((id, have2, Plan(keys, ct, typeName, item, haveEntry, Mode.All, dangling, notes, child: true),
                    PlanChildren(keys, ct, item, haveEntry, dangling, notes)));
            }
            var gone = current.Where(kv => plans.All(p => p.Id != kv.Key)).Select(kv => kv.Value).ToList();
            var accessor = nav.GetCollectionAccessor()!;

            steps.Add((ctx, parent, at) =>
            {
                foreach (var g in gone) { accessor.Remove(parent.Entity, g); ctx.Remove(g); }
                foreach (var (id, have, sets, deeper) in plans)
                {
                    EntityEntry e;
                    if (have is null)
                    {
                        var row = Activator.CreateInstance(ct.ClrType, nonPublic: true)!;
                        accessor.Add(parent.Entity, row, forMaterialization: false);
                        e = ctx.Entry(row);
                        e.State = EntityState.Added;
                        e.Property(SyncModel.SyncIdProperty).CurrentValue = id;
                    }
                    else e = ctx.Entry(have);
                    foreach (var (name, value) in sets) e.Property(name).CurrentValue = value;
                    e.Property(SyncModel.LastChangedAtProperty).CurrentValue = at;
                    deeper?.Invoke(ctx, e, at);
                }
            });
        }
        return steps.Count == 0 ? null : (ctx, parent, at) => { foreach (var s in steps) s(ctx, parent, at); };
    }

    private static async Task<EntityEntry?> Find(AppDbContext db, IEntityType et, int restaurantId, Guid id, CancellationToken ct)
    {
        var task = (Task<object?>)FindOpen.MakeGenericMethod(et.ClrType).Invoke(null, new object[] { db, et, restaurantId, id, ct })!;
        var row = await task;
        return row is null ? null : db.Entry(row);
    }

    private static async Task<object?> FindTyped<T>(AppDbContext db, IEntityType et, int restaurantId, Guid id, CancellationToken ct) where T : class
    {
        IQueryable<T> q = db.Set<T>().Where(e => EF.Property<Guid>(e, SyncModel.SyncIdProperty) == id);
        q = typeof(T) == typeof(Restaurant)
            ? q.Where(e => EF.Property<int>(e, "Id") == restaurantId)
            : q.Where(e => EF.Property<int>(e, "RestaurantId") == restaurantId);
        foreach (var path in SyncModel.IncludePaths(et)) q = q.Include(path);
        return await q.AsSplitQuery().FirstOrDefaultAsync(ct);
    }
}
