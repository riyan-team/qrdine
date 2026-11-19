using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;

namespace QrDine.Application.Sync;

/// <summary>
/// Pairing a restaurant's PC, and the PC's sync calls: hello, first download (snapshot), changes
/// since N (pull) and sending its own changes (push). Every device call is held to the device's own
/// restaurant: the restaurant comes from the device, never from the request.
/// </summary>
public class SyncService
{
    public const int MaxActiveDevices = 5;
    public const int MaxBatch = 500;
    public const int MaxPayloadChars = 64 * 1024;
    private const string KeyPrefix = "dk_";

    private readonly IAppDbContext _db;
    private readonly ISyncStore _store;
    private readonly SyncSettings _settings;

    public SyncService(IAppDbContext db, ISyncStore store, SyncSettings settings)
    {
        _db = db;
        _store = store;
        _settings = settings;
    }

    // ---------- pairing: the owner, signed in to their restaurant ----------

    /// <summary>Pairs a new device for the current restaurant. The key is shown this once; only its hash is kept.</summary>
    public async Task<PairedDevice> Pair(string? name, string createdBy, CancellationToken ct = default)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length is 0 or > 80) throw new DomainException("Give the device a name of up to 80 characters, e.g. \"Counter PC\".");
        if (await _db.SyncDevices.CountAsync(d => d.RevokedAt == null, ct) >= MaxActiveDevices)
            throw new DomainException($"This restaurant already has {MaxActiveDevices} paired devices. Remove one first.");

        var key = KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var device = new SyncDevice { Name = clean, KeyHash = Hash(key), CreatedBy = createdBy };
        _db.SyncDevices.Add(device);
        await _db.SaveChangesAsync(ct);
        return new PairedDevice(device.Id, device.Name, key);
    }

    public async Task<List<SyncDeviceRow>> Devices(CancellationToken ct = default) =>
        await _db.SyncDevices.OrderBy(d => d.Id)
            .Select(d => new SyncDeviceRow(d.Id, d.Name, d.CreatedAt, d.CreatedBy, d.LastSeenAt, d.RevokedAt))
            .ToListAsync(ct);

    /// <summary>Ends a device's access at once. Its key stops working on the next call.</summary>
    public async Task Revoke(int id, CancellationToken ct = default)
    {
        var d = await _db.SyncDevices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Device not found.");
        d.RevokedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ---------- a device signing in ----------

    /// <summary>The active device this key belongs to, or null. Runs before any restaurant is known.</summary>
    public async Task<SyncDevice?> Authenticate(string? key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal) || key.Length > 100) return null;
        var hash = Hash(key.Trim());
        return await _db.SyncDevices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.KeyHash == hash && d.RevokedAt == null, ct);
    }

    /// <summary>Notes that the device was here, at most once a minute so a busy PC doesn't write on every call.</summary>
    public async Task Seen(SyncDevice d, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (d.LastSeenAt is DateTime last && now - last < TimeSpan.FromMinutes(1)) return;
        d.LastSeenAt = now;
        await _db.SaveChangesAsync(ct);
    }

    // ---------- a device's calls ----------

    public async Task<SyncHello> Hello(SyncDevice d, Restaurant r, CancellationToken ct = default) =>
        new(r.Id, r.Slug, r.Name, d.Id, d.Name, DateTime.UtcNow, await Head(d.RestaurantId, ct), _store.RootTypes);

    /// <summary>
    /// The newest change-list line for the restaurant. A new device notes this, downloads every type
    /// with <see cref="Snapshot"/>, then pulls from here, so nothing made during the download is missed.
    /// </summary>
    public async Task<long> Head(int restaurantId, CancellationToken ct = default) =>
        await _db.SyncChanges.Where(c => c.RestaurantId == restaurantId).MaxAsync(c => (long?)c.Id, ct) ?? 0;

    public async Task<SyncSnapshotPage> Snapshot(SyncDevice d, string? type, int after, int limit, CancellationToken ct = default)
    {
        var t = KnownType(type);
        var rows = await _store.Page(t, d.RestaurantId, Math.Max(0, after), Math.Clamp(limit, 1, MaxBatch), ct);
        return new SyncSnapshotPage(t, rows.Select(x => x.Row).ToList(), rows.Count == Math.Clamp(limit, 1, MaxBatch) ? rows[^1].Id : null);
    }

    /// <summary>
    /// What changed after line <paramref name="since"/>: each changed row once, as it is now. A row
    /// that no longer exists comes back as a delete. <c>Next</c> is where to pull from next time.
    /// </summary>
    public async Task<SyncPullPage> Pull(SyncDevice d, long since, int limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, MaxBatch);
        var settled = DateTime.UtcNow.AddSeconds(-Math.Max(0, _settings.SettleSeconds));
        var batch = await _db.SyncChanges
            .Where(c => c.RestaurantId == d.RestaurantId && c.Id > since && c.ChangedAt <= settled)
            .OrderBy(c => c.Id).Take(take).ToListAsync(ct);

        var latest = batch.GroupBy(c => (c.EntityType, c.SyncId)).Select(g => g.MaxBy(c => c.Id)!).OrderBy(c => c.Id).ToList();
        var rows = new Dictionary<(string, Guid), SyncRow>();
        var unknown = new HashSet<string>();
        foreach (var group in latest.Where(c => c.Op == SyncOp.Upsert).GroupBy(c => c.EntityType))
        {
            var loaded = await _store.Load(group.Key, d.RestaurantId, group.Select(c => c.SyncId).ToList(), ct);
            if (loaded is null) { unknown.Add(group.Key); continue; }
            foreach (var (id, row) in loaded) rows[(group.Key, id)] = row;
        }

        var changes = new List<SyncPulledChange>();
        foreach (var c in latest)
        {
            if (unknown.Contains(c.EntityType)) continue;   // never tell a device to delete what this build can't read
            if (c.Op == SyncOp.Upsert && rows.TryGetValue((c.EntityType, c.SyncId), out var row))
                changes.Add(new(c.Id, c.EntityType, c.SyncId, SyncOp.Upsert, c.ChangedAt, row.LastChangedAt, row.Data));
            else
                changes.Add(new(c.Id, c.EntityType, c.SyncId, SyncOp.Delete, c.ChangedAt, null, null));
        }
        return new SyncPullPage(changes, batch.Count > 0 ? batch[^1].Id : since, batch.Count == take);
    }

    /// <summary>
    /// Keeps a device's changes in the inbox, once per change id. Phase 1 stores them only;
    /// applying them to the restaurant's data comes with the PC app.
    /// </summary>
    public async Task<SyncPushResult> Push(SyncDevice d, IReadOnlyList<SyncPushChange>? changes, CancellationToken ct = default)
    {
        if (changes is null || changes.Count == 0) throw new DomainException("Send at least one change.");
        if (changes.Count > MaxBatch) throw new DomainException($"Send at most {MaxBatch} changes at a time.");

        var fresh = new List<SyncInboxEntry>();
        var seen = new HashSet<Guid>();
        foreach (var c in changes)
        {
            if (c.ChangeId == Guid.Empty || c.SyncId == Guid.Empty) throw new DomainException("Every change needs a changeId and a syncId.");
            var type = KnownType(c.Type);
            if (!SyncOp.IsValid(c.Op)) throw new DomainException($"Change {c.ChangeId}: op must be {SyncOp.Upsert} or {SyncOp.Delete}.");
            if (c.ChangedAt == default) throw new DomainException($"Change {c.ChangeId}: changedAt is missing.");
            string? payload = null;
            if (c.Op == SyncOp.Upsert)
            {
                if (c.Data is not { ValueKind: System.Text.Json.JsonValueKind.Object } data)
                    throw new DomainException($"Change {c.ChangeId}: an upsert needs the row as a JSON object in data.");
                payload = data.GetRawText();
                if (payload.Length > MaxPayloadChars) throw new DomainException($"Change {c.ChangeId} is larger than 64 KB.");
            }
            if (!seen.Add(c.ChangeId)) continue;
            fresh.Add(new SyncInboxEntry
            {
                RestaurantId = d.RestaurantId, DeviceId = d.Id, ChangeId = c.ChangeId, EntityType = type,
                SyncId = c.SyncId, Op = c.Op!, Payload = payload,
                ClientChangedAt = AsUtc(c.ChangedAt),
            });
        }

        // One restaurant's pushes take turns, so a batch sent twice at once (a PC retrying while its
        // first try is still saving) is stored once without the database refusing the second. The
        // unique index on (RestaurantId, ChangeId) still holds if two copies of the app ever run.
        if (!await PushTurns.Enter(d.RestaurantId, TimeSpan.FromSeconds(20), ct)) return new SyncPushResult(0, 0, Busy: true);
        try
        {
            var ids = fresh.Select(x => x.ChangeId).ToList();
            var had = (await _db.SyncInbox.IgnoreQueryFilters()
                .Where(x => x.RestaurantId == d.RestaurantId && ids.Contains(x.ChangeId))
                .Select(x => x.ChangeId).ToListAsync(ct)).ToHashSet();
            var add = fresh.Where(x => !had.Contains(x.ChangeId)).ToList();
            if (add.Count == 0) return new SyncPushResult(0, changes.Count);

            _db.SyncInbox.AddRange(add);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return new SyncPushResult(0, 0, Busy: true); }
            return new SyncPushResult(add.Count, changes.Count - add.Count);
        }
        finally { PushTurns.Leave(d.RestaurantId); }
    }

    // Separate from the floor's gate: storing a PC's batch never holds up a cashier.
    private static readonly RestaurantGate PushTurns = new();

    private string KnownType(string? type)
    {
        var t = (type ?? "").Trim();
        return _store.RootTypes.Contains(t) ? t : throw new DomainException($"Unknown type \"{t}\".");
    }

    // Devices send UTC. A time without a zone is taken as UTC too, never as the server's own clock (IST on Hostworld).
    private static DateTime AsUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Local => t.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(t, DateTimeKind.Utc),
        _ => t,
    };

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}
