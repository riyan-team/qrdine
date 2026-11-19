using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using QrDine.Domain.Common;
using QrDine.Domain.Restaurants;

namespace QrDine.Infrastructure.Sync;

/// <summary>
/// Which tables take part in sync, and the two columns each of them gets.
///
/// A <b>root</b> is a restaurant-owned row that syncs on its own (an order, a bill, a menu item,
/// the restaurant's settings). A <b>child</b> belongs to a root and always travels with it: order
/// items and their modifiers go with their order, invoice lines with their bill, variants and
/// modifier options with their menu item. The change list names roots only; a changed child marks
/// its root as changed.
///
/// Both columns are shadow properties, so the domain classes don't change:
/// <list type="bullet">
/// <item><c>SyncId</c>: the row's id on every copy. The int ids stay, but a PC and the server can
/// both make "order 105", so sync matches rows by this instead. On Postgres it has a database
/// default, so a row inserted by an older build (or by hand) still gets one.</item>
/// <item><c>LastChangedAt</c>: when the row last changed, for "newest change wins". Null for rows
/// last saved before sync existed.</item>
/// </list>
/// </summary>
public static class SyncModel
{
    public const string SyncIdProperty = "SyncId";
    public const string LastChangedAtProperty = "LastChangedAt";

    // Platform-level data (operators, plans, subscription payments), menu photo bytes, and the sync
    // tables themselves are never synced to a restaurant's PC.
    private static readonly HashSet<Type> NeverSynced = new()
    {
        typeof(Domain.Identity.PlatformAdmin), typeof(Domain.Subscriptions.Plan),
        typeof(Domain.Subscriptions.SubscriptionPayment), typeof(Domain.Files.StoredFile),
        typeof(Domain.Sync.SyncDevice), typeof(Domain.Sync.SyncChange), typeof(Domain.Sync.SyncInboxEntry),
    };

    /// <summary>Adds the sync columns to every synced table. Call at the end of OnModelCreating.</summary>
    public static void Configure(ModelBuilder b, bool postgres)
    {
        var synced = new HashSet<IMutableEntityType>(b.Model.GetEntityTypes().Where(t => IsRootType(t.ClrType)));
        // Children: rows that cascade from a synced row through one of its collections, to any depth.
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var t in b.Model.GetEntityTypes())
            {
                if (synced.Contains(t) || NeverSynced.Contains(t.ClrType)) continue;
                if (t.GetForeignKeys().Any(fk => fk.DeleteBehavior == DeleteBehavior.Cascade
                        && synced.Contains(fk.PrincipalEntityType) && fk.PrincipalToDependent is { IsCollection: true }))
                {
                    synced.Add(t);
                    grew = true;
                }
            }
        }

        foreach (var t in synced)
        {
            var e = b.Entity(t.ClrType);
            var id = e.Property<Guid>(SyncIdProperty);
            if (postgres) id.HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(SyncIdProperty).IsUnique();
            e.Property<DateTime?>(LastChangedAtProperty);
        }
    }

    private static bool IsRootType(Type clr) =>
        !NeverSynced.Contains(clr) && (clr == typeof(Restaurant) || typeof(ITenantOwned).IsAssignableFrom(clr));

    // The sync tables have a SyncId of their own (the row a line is about), so the column alone doesn't decide.
    public static bool IsSynced(IReadOnlyEntityType t) => !NeverSynced.Contains(t.ClrType) && t.FindProperty(SyncIdProperty) is not null;

    public static bool IsRoot(IReadOnlyEntityType t) => IsSynced(t) && IsRootType(t.ClrType);

    /// <summary>The name a type goes by in the change list and the sync API, e.g. "Order".</summary>
    public static string NameOf(IReadOnlyEntityType t) => t.ClrType.Name;

    private static readonly ConcurrentDictionary<IReadOnlyEntityType, IReadOnlyForeignKey> ParentKeys = new();

    /// <summary>For a child type, the foreign key to the row it belongs to.</summary>
    public static IReadOnlyForeignKey ParentKey(IReadOnlyEntityType child) => ParentKeys.GetOrAdd(child, t =>
        t.GetForeignKeys().First(fk => fk.DeleteBehavior == DeleteBehavior.Cascade
            && IsSynced(fk.PrincipalEntityType) && fk.PrincipalToDependent is { IsCollection: true }));

    /// <summary>The child collections that travel with a row, as Include paths ("Items", "Items.Modifiers").</summary>
    public static IEnumerable<string> IncludePaths(IReadOnlyEntityType t, string prefix = "")
    {
        foreach (var nav in t.GetNavigations())
        {
            if (!nav.IsCollection || nav.ForeignKey.DeleteBehavior != DeleteBehavior.Cascade) continue;
            if (!IsSynced(nav.TargetEntityType) || IsRoot(nav.TargetEntityType)) continue;
            var path = prefix + nav.Name;
            var deeper = IncludePaths(nav.TargetEntityType, path + ".").ToList();
            if (deeper.Count == 0) yield return path;
            else foreach (var d in deeper) yield return d;
        }
    }
}
