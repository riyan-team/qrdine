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
/// since N (pull), sending its own changes (push, applied at once with the sync rules) and taking over
/// the counter. Every device call is held to the device's own restaurant: the restaurant comes from
/// the device, never from the request.
/// </summary>
public class SyncService
{
    /// <summary>
    /// One counter PC per restaurant: it gives out the bill numbers, so two would repeat them. Phones,
    /// tablets and the kitchen screen join that PC over the restaurant Wi-Fi; they aren't paired.
    /// </summary>
    public const int MaxActiveDevices = 1;
    public const int MaxBatch = 500;
    public const int MaxPayloadChars = 256 * 1024;
    private const string KeyPrefix = "dk_";

    private readonly IAppDbContext _db;
    private readonly ISyncStore _store;
    private readonly SyncSettings _settings;
    private readonly ISyncApplier? _applier;

    public SyncService(IAppDbContext db, ISyncStore store, SyncSettings settings, ISyncApplier? applier = null)
    {
        _db = db;
        _store = store;
        _settings = settings;
        _applier = applier;
    }

    // ---------- pairing: the owner, signed in to their restaurant ----------

    /// <summary>Pairs a new device for the current restaurant. The key is shown this once; only its hash is kept.</summary>
    public async Task<PairedDevice> Pair(string? name, string createdBy, CancellationToken ct = default)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length is 0 or > 80) throw new DomainException("Give the device a name of up to 80 characters, e.g. \"Counter PC\".");
        if (await _db.SyncDevices.CountAsync(d => d.RevokedAt == null, ct) >= MaxActiveDevices)
            throw new DomainException("This restaurant already has its counter PC. Remove it first (let it finish sending its changes), then pair the new one.");

        var key = KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var device = new SyncDevice { Name = clean, KeyHash = Hash(key), CreatedBy = createdBy };
        _db.SyncDevices.Add(device);
        await _db.SaveChangesAsync(ct);
        return new PairedDevice(device.Id, device.Name, key);
    }

    public async Task<List<SyncDeviceRow>> Devices(CancellationToken ct = default)
    {
        var devices = await _db.SyncDevices.OrderBy(d => d.Id).ToListAsync(ct);
        var counts = await _db.SyncInbox
            .Where(x => x.Status == SyncInboxStatus.Pending || x.Status == SyncInboxStatus.Failed)
            .GroupBy(x => new { x.DeviceId, x.Status }).Select(g => new { g.Key.DeviceId, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        var lastFailed = await _db.SyncInbox.Where(x => x.Status == SyncInboxStatus.Failed)
            .GroupBy(x => x.DeviceId).Select(g => g.OrderByDescending(x => x.Id).Select(x => new { x.DeviceId, x.EntityType, x.Note }).First()).ToListAsync(ct);
        return devices.Select(d => new SyncDeviceRow(d.Id, d.Name, d.CreatedAt, d.CreatedBy, d.LastSeenAt, d.RevokedAt, d.CounterSince,
            counts.Where(c => c.DeviceId == d.Id && c.Status == SyncInboxStatus.Pending).Sum(c => c.N),
            counts.Where(c => c.DeviceId == d.Id && c.Status == SyncInboxStatus.Failed).Sum(c => c.N),
            lastFailed.Where(f => f.DeviceId == d.Id).Select(f => $"{f.EntityType}: {f.Note}").FirstOrDefault())).ToList();
    }

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
        new(r.Id, r.Slug, r.Name, d.Id, d.Name, DateTime.UtcNow, await Head(d.RestaurantId, ct), _store.InDependencyOrder, d.CounterSince,
            MaxBatch, MaxPayloadChars);

    /// <summary>
    /// The PC has its copy and runs the counter from now on: the server stops making orders, bills and
    /// other floor records for the restaurant (see the counter-on-the-PC rule in AppDbContext). Saying
    /// it again changes nothing. Returns the head, so the PC can catch up on anything made up to now.
    /// </summary>
    /// <param name="rejoin">A PC that already ran this counter, back with a new key after its old one was removed.
    /// It keeps its own records, so it may take the counter back only if nothing was sold online in between:
    /// those bills would clash with the PC's bill numbers. A fresh PC downloads everything first and needs no check.</param>
    public async Task<long> TakeOver(SyncDevice d, bool rejoin = false, CancellationToken ct = default)
    {
        var tracked = await _db.SyncDevices.FirstAsync(x => x.Id == d.Id, ct);
        if (tracked.CounterSince is null && rejoin)
        {
            var removedAt = await _db.SyncDevices
                .Where(x => x.RestaurantId == d.RestaurantId && x.RevokedAt != null && x.CounterSince != null)
                .MaxAsync(x => x.RevokedAt, ct);
            var floor = SyncRules.FloorTypes.ToList();
            if (removedAt is DateTime gap && await _db.SyncChanges.AnyAsync(c => c.RestaurantId == d.RestaurantId
                    && c.OriginDeviceId == null && c.ChangedAt > gap && floor.Contains(c.EntityType), ct))
                throw new DomainException("Orders, bills or khata entries were made online after the last PC was removed, so this PC " +
                    "can't take the counter back without mixing bill numbers. Connect a fresh PC instead (Dine In with an empty data folder).");
        }
        if (tracked.CounterSince is null)
        {
            tracked.CounterSince = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return await Head(d.RestaurantId, ct);
    }

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

        // A row whose every line in this batch is the device's own change, applied here, isn't sent back to it.
        var latest = batch.GroupBy(c => (c.EntityType, c.SyncId))
            .Where(g => g.Any(c => c.OriginDeviceId != d.Id))
            .Select(g => g.MaxBy(c => c.Id)!).OrderBy(c => c.Id).ToList();
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
    /// Keeps a device's changes in the inbox, once per change id, then applies what is waiting with the
    /// sync rules (newest per row only; older waiting copies of the same row are passed over).
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
            if (add.Count > 0)
            {
                _db.SyncInbox.AddRange(add);
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return new SyncPushResult(0, 0, Busy: true); }
            }
            var applied = await ApplyWaiting(d, ct);
            return new SyncPushResult(add.Count, changes.Count - add.Count, false, applied.Applied, applied.Skipped, applied.Failed, applied.Notes);
        }
        finally { PushTurns.Leave(d.RestaurantId); }
    }

    /// <summary>Applies the device's waiting inbox lines, newest per row, and records how each went.</summary>
    private async Task<(int Applied, int Skipped, int Failed, List<string> Notes)> ApplyWaiting(SyncDevice d, CancellationToken ct)
    {
        if (_applier is null) return (0, 0, 0, new());
        var waiting = await _db.SyncInbox
            .Where(x => x.DeviceId == d.Id && x.Status == SyncInboxStatus.Pending)
            .OrderBy(x => x.Id).Take(MaxBatch * 4).ToListAsync(ct);
        if (waiting.Count == 0) return (0, 0, 0, new());

        var now = DateTime.UtcNow;
        var newest = waiting.GroupBy(x => (x.EntityType, x.SyncId)).Select(g => g.MaxBy(x => x.Id)!).ToHashSet();
        foreach (var older in waiting.Where(x => !newest.Contains(x)))
        {
            older.Status = SyncInboxStatus.Skipped;
            older.Note = "A newer copy of the same row came after it.";
            older.AppliedAt = now;
        }

        var incoming = newest.OrderBy(x => x.Id).Select(x => new SyncIncoming(x.Id, x.EntityType, x.SyncId, x.Op, x.ClientChangedAt,
            x.Payload is null ? null : System.Text.Json.JsonDocument.Parse(x.Payload).RootElement.Clone())).ToList();
        var results = await _applier.Apply(d.RestaurantId, incoming, new SyncApplyOptions(SyncApplySide.OnServer, d.Id), ct);

        var byId = newest.ToDictionary(x => x.Id);
        var notes = new List<string>();
        foreach (var r in results)
        {
            var row = byId[r.Seq];
            row.Attempts++;
            row.Status = r.Status;
            row.Note = r.Note is { Length: > 400 } n ? n[..400] : r.Note;
            row.AppliedAt = now;
            if (r.Status == SyncInboxStatus.Failed && notes.Count < 10) notes.Add($"{r.Type} {r.SyncId}: {r.Note}");
        }
        await _db.SaveChangesAsync(ct);
        return (results.Count(r => r.Status == SyncInboxStatus.Applied), results.Count(r => r.Status == SyncInboxStatus.Skipped),
            results.Count(r => r.Status == SyncInboxStatus.Failed), notes);
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
