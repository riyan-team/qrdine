using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Application.Sync;
using QrDine.Domain.Files;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using QrDine.Infrastructure.Sync;

namespace QrDine.Api.Pc;

/// <summary>What the PC remembers about its online restaurant (DineIn-data/sync.json). The device key is in here: keep the folder private.</summary>
public sealed class PcSyncState
{
    public string Server { get; set; } = "";
    public string Key { get; set; } = "";
    public Guid InstallId { get; set; } = Guid.NewGuid();
    public string Stage { get; set; } = PcSyncStages.Connecting;
    public string Slug { get; set; } = "";
    public string RestaurantName { get; set; } = "";
    public long PulledUpTo { get; set; }
    public long PushedUpTo { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CounterSince { get; set; }
}

public static class PcSyncStages
{
    public const string Connecting = "connecting";   // downloading the restaurant; nothing else works yet
    public const string Ready = "ready";             // the counter runs here; changes go up and come down every few seconds
}

/// <summary>What the screens show about the online copy.</summary>
/// <param name="KeyRefused">The online server no longer knows this PC's key (it was removed online). The PC keeps working
/// on its own; a new key for the same restaurant reconnects it (see <see cref="PcSync.Connect"/>).</param>
public sealed record PcSyncStatus(string State, string? Server, string? Restaurant, bool Online, DateTime? LastSyncAt, string? LastError,
    int Waiting, int ServerFailed, IReadOnlyList<string> ServerNotes, string? Progress, DateTime? CounterSince, bool KeyRefused = false);

/// <summary>The online server answered 401: this PC's key was removed there.</summary>
public sealed class PcKeyRefusedException() : InvalidOperationException(
    "This PC was removed online, so it no longer sends or receives changes. It keeps working here. Pair it again online and enter the new key.");

/// <summary>
/// Hybrid: this PC runs the restaurant's counter and keeps its online copy in step.
/// <list type="number">
/// <item><b>Connect</b> (a PC with no restaurant yet, given the server address and the device key the owner
/// paired online): hello, then every type in dependency order from the snapshot, then "take over" (the
/// server stops making bills), then catch up on anything made meanwhile. Only then do the screens open.</item>
/// <item><b>Every few seconds</b> while ready: send this PC's own changes (photos first), then fetch the
/// server's (settings changed from home). No internet is fine: the PC keeps working and catches up later.</item>
/// </list>
/// </summary>
public sealed class PcSync : BackgroundService
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Regex FileUrl = new("^/api/files/([0-9a-f]{32})$", RegexOptions.Compiled);
    private const int ChunkLines = 1000;

    private readonly PcPaths _paths;
    private readonly IServiceScopeFactory _scopes;
    private readonly SyncSettings _settings;
    private readonly ISyncApplier _applier;
    private readonly PcLicence _licence;
    private readonly ILogger<PcSync> _log;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly HttpMessageHandler? _handler;
    private PcSyncState? _state;
    private bool _online;
    private DateTime? _lastSyncAt;
    private string? _lastError;
    private int _serverFailed;
    private List<string> _serverNotes = new();
    private string? _progress;
    private bool _keyRefused;

    public TimeSpan Every { get; set; } = TimeSpan.FromSeconds(5);

    public PcSync(PcPaths paths, IServiceScopeFactory scopes, SyncSettings settings, ISyncApplier applier, PcLicence licence,
        ILogger<PcSync> log, HttpMessageHandler? handler = null)
    {
        _paths = paths;
        _scopes = scopes;
        _settings = settings;
        _applier = applier;
        _licence = licence;
        _log = log;
        _handler = handler;
        _state = Load();
        if (_state is not null) _settings.RecordOnThisPc = true;
    }

    private string StateFile => Path.Combine(_paths.DataDir, "sync.json");

    public bool Connected => _state is not null;
    public bool Downloading => _state?.Stage == PcSyncStages.Connecting;

    public async Task<PcSyncStatus> Status(CancellationToken ct = default)
    {
        var s = _state;
        if (s is null) return new PcSyncStatus("off", null, null, false, null, null, 0, 0, Array.Empty<string>(), null, null);
        var waiting = 0;
        if (s.Stage == PcSyncStages.Ready)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            waiting = await db.SyncChanges.IgnoreQueryFilters().CountAsync(c => c.Id > s.PushedUpTo && c.OriginDeviceId == null, ct);
        }
        return new PcSyncStatus(s.Stage, s.Server, s.RestaurantName, _online, _lastSyncAt, _lastError, waiting, _serverFailed, _serverNotes,
            _progress, s.CounterSince, _keyRefused);
    }

    public void Wake() => _wake.Release();

    // ---------------------------------------------------------------- connecting

    /// <summary>
    /// Checks the address and key, then downloads the restaurant in the background. On a PC that is already connected,
    /// a new key for the same restaurant reconnects it instead (see <see cref="Rejoin"/>).
    /// </summary>
    public async Task Connect(string? server, string? key, CancellationToken ct)
    {
        var url = (server ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && !(u.Scheme == "http" && u.IsLoopback)))
            throw new Domain.Common.DomainException("Enter the online address, for example https://qrdine.mystay.live");
        var k = (key ?? "").Trim();
        if (!k.StartsWith("dk_", StringComparison.Ordinal)) throw new Domain.Common.DomainException("Paste the PC key shown when you paired this PC online (it starts with dk_).");
        if (_state is { } current)
        {
            if (current.Stage != PcSyncStages.Ready) throw new Domain.Common.DomainException("This PC is still downloading its restaurant.");
            await Rejoin(current, url, k, ct);
            return;
        }
        using (var scope = _scopes.CreateScope())
            if (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Restaurants.IgnoreQueryFilters().AnyAsync(ct))
                throw new Domain.Common.DomainException("This PC already has its own restaurant. Connecting works on a PC with no restaurant yet.");

        var state = new PcSyncState { Server = url, Key = k };
        SyncHello hello;
        using (var http = Client(state))
        {
            HttpResponseMessage res;
            try { res = await http.GetAsync("api/sync/hello", ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            { throw new Domain.Common.DomainException("Can't reach the online address. Check the internet and the address, then try again."); }
            if (res.StatusCode == HttpStatusCode.Unauthorized) throw new Domain.Common.DomainException("That PC key isn't paired (or it was removed). Pair this PC again online and paste the new key.");
            if (res.StatusCode == HttpStatusCode.NotFound) throw new Domain.Common.DomainException("The online server doesn't have restaurant PCs switched on yet.");
            if (!res.IsSuccessStatusCode) throw new Domain.Common.DomainException($"The online server said {(int)res.StatusCode}: {await Detail(res, ct)}");
            hello = (await res.Content.ReadFromJsonAsync<SyncHello>(Web, ct))!;
        }
        state.Slug = hello.Slug;
        state.RestaurantName = hello.RestaurantName;
        state.PulledUpTo = hello.Head;
        _state = state;
        _settings.RecordOnThisPc = true;
        Save(state);
        _progress = "Starting the download";
        Wake();
    }

    /// <summary>
    /// A connected PC whose key was removed online (by mistake, or because it leaked), given a new key for the same
    /// restaurant: it keeps everything it has, sends what is still waiting, and takes the counter back. The server
    /// refuses if orders or bills were made online in between, since their numbers would clash with this PC's.
    /// </summary>
    private async Task Rejoin(PcSyncState s, string url, string key, CancellationToken ct)
    {
        if (!string.Equals(url, s.Server, StringComparison.OrdinalIgnoreCase))
            throw new Domain.Common.DomainException($"This PC is connected to {s.Server}. Use a key from there.");
        if (key == s.Key) throw new Domain.Common.DomainException("This PC is already connected with that key.");
        using var http = Client(new PcSyncState { Server = s.Server, Key = key });
        HttpResponseMessage res;
        try { res = await http.GetAsync("api/sync/snapshot?type=Restaurant&after=0&limit=1", ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { throw new Domain.Common.DomainException("Can't reach the online address. Check the internet, then try again."); }
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new Domain.Common.DomainException("That PC key isn't paired (or it was removed). Pair this PC again online and paste the new key.");
        if (!res.IsSuccessStatusCode) throw new Domain.Common.DomainException($"The online server said {(int)res.StatusCode}: {await Detail(res, ct)}");
        var theirs = (await res.Content.ReadFromJsonAsync<SnapshotPage>(Web, ct))!.Items.Single().SyncId;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mine = await db.Restaurants.IgnoreQueryFilters().FirstAsync(ct);
            if ((Guid)db.Entry(mine).Property(SyncModel.SyncIdProperty).CurrentValue! != theirs)
                throw new Domain.Common.DomainException("That key is for a different restaurant than the one on this PC.");
        }
        var took = await http.PostAsync("api/sync/takeover?rejoin=true", null, ct);
        if (!took.IsSuccessStatusCode) throw new Domain.Common.DomainException(await Detail(took, ct));

        await _turn.WaitAsync(ct);
        try
        {
            s.Key = key;
            s.CounterSince ??= DateTime.UtcNow;
            Save(s);
            _keyRefused = false;
            _lastError = null;
        }
        finally { _turn.Release(); }
        Wake();
    }

    private async Task Download(PcSyncState s, CancellationToken ct)
    {
        using var http = Client(s);
        var hello = (await (await Send(() => http.GetAsync("api/sync/hello", ct))).Content.ReadFromJsonAsync<SyncHello>(Web, ct))!;
        int rid;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var local = await db.Restaurants.IgnoreQueryFilters().FirstOrDefaultAsync(ct);
            if (local is null)
            {
                var first = (await (await Send(() => http.GetAsync("api/sync/snapshot?type=Restaurant&after=0&limit=1", ct)))
                    .Content.ReadFromJsonAsync<SnapshotPage>(Web, ct))!;
                var row = first.Items.Single();
                local = new Restaurant { Slug = hello.Slug, Name = hello.RestaurantName };
                db.Restaurants.Add(local);
                db.Entry(local).Property(SyncModel.SyncIdProperty).CurrentValue = row.SyncId;
                await db.SaveChangesAsync(ct);
            }
            rid = local.Id;
        }

        var total = 0;
        foreach (var type in hello.Types)
        {
            int? after = 0;
            while (after is int a)
            {
                _progress = $"Downloading {Words(type)} ({total} rows so far)";
                var page = (await (await Send(() => http.GetAsync($"api/sync/snapshot?type={type}&after={a}&limit=500", ct)))
                    .Content.ReadFromJsonAsync<SnapshotPage>(Web, ct))!;
                var incoming = page.Items.Select((x, i) => new SyncIncoming(i, type, x.SyncId, SyncOp.Upsert, x.LastChangedAt, x.Data)).ToList();
                Report(await _applier.Apply(rid, incoming, new SyncApplyOptions(SyncApplySide.OnPc, SyncChange.FromServer, TakeEverything: true), ct), "download");
                total += page.Items.Count;
                after = page.Next;
            }
        }

        _progress = "Taking over the counter";
        var took = await Send(() => http.PostAsync("api/sync/takeover", null, ct));
        s.CounterSince = DateTime.UtcNow;
        // Anything the server made while this PC was downloading (a bill settled online a moment ago) comes down
        // now, as the server has it. The pause lets saves still committing on the server show up first.
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        _progress = "Catching up on the last few changes";
        await PullAll(http, s, rid, takeEverything: true, ct);

        _progress = "Fetching menu photos";
        await FetchFiles(http, rid, ct);

        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            s.PushedUpTo = await db.SyncChanges.IgnoreQueryFilters().MaxAsync(c => (long?)c.Id, ct) ?? 0;
            await PcLicenceApplier.Apply(db, _licence, _log, ct);
        }
        s.Stage = PcSyncStages.Ready;
        Save(s);
        _progress = null;
        _log.LogInformation("Connected to {Server}: {Rows} rows downloaded, the counter runs on this PC now", s.Server, total);
    }

    // ---------------------------------------------------------------- every few seconds

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RoundOnce(stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            try { await _wake.WaitAsync(Every, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One round: download if still connecting, otherwise send then fetch. Public for tests.</summary>
    public async Task RoundOnce(CancellationToken ct)
    {
        var s = _state;
        if (s is null) return;
        await _turn.WaitAsync(ct);
        try
        {
            if (s.Stage == PcSyncStages.Connecting) await Download(s, ct);
            else
            {
                int rid;
                using (var scope = _scopes.CreateScope())
                    rid = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Restaurants.IgnoreQueryFilters().Select(r => r.Id).FirstAsync(ct);
                using var http = Client(s);
                await PushAll(http, s, rid, ct);
                await PullAll(http, s, rid, takeEverything: false, ct);
                await Prune(s, ct);
            }
            _online = true;
            _lastSyncAt = DateTime.UtcNow;
            _lastError = null;
            _keyRefused = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _online = false;
            _lastError = "No internet, or the online server can't be reached. This PC keeps working; changes go up when it is back.";
        }
        catch (PcKeyRefusedException ex)
        {
            _online = true;
            _keyRefused = true;
            _lastError = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastError = ex.Message;
            _log.LogWarning(ex, "Sync round failed");
        }
        finally { _turn.Release(); }
    }

    private async Task PushAll(HttpClient http, PcSyncState s, int rid, CancellationToken ct)
    {
        for (var round = 0; round < 50; round++)
        {
            List<SyncChange> lines;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                lines = await db.SyncChanges.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.Id > s.PushedUpTo).OrderBy(c => c.Id).Take(ChunkLines).ToListAsync(ct);
            }
            if (lines.Count == 0) return;
            var mine = lines.Where(c => c.OriginDeviceId == null).ToList();
            var upTo = lines[^1].Id;
            if (mine.Count > 0)
            {
                var changes = await Changes(s, rid, mine, ct);
                await UploadFiles(http, changes, ct);
                foreach (var batch in Batches(changes))
                {
                    var res = await http.PostAsJsonAsync("api/sync/push", new { changes = batch }, Web, ct);
                    if (res.StatusCode == HttpStatusCode.Conflict) return;   // the same batch was saving; next round
                    if (res.StatusCode == HttpStatusCode.Unauthorized) throw new PcKeyRefusedException();
                    if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The online server refused the changes ({(int)res.StatusCode}): {await Detail(res, ct)}");
                    var result = (await res.Content.ReadFromJsonAsync<SyncPushResult>(Web, ct))!;
                    _serverFailed += result.Failed;
                    if (result.Notes is { Count: > 0 }) _serverNotes = _serverNotes.Concat(result.Notes).TakeLast(10).ToList();
                }
            }
            s.PushedUpTo = upTo;
            Save(s);
        }
    }

    /// <summary>This PC's changed rows as they are now, newest line per row, ready to send.</summary>
    private async Task<List<object>> Changes(PcSyncState s, int rid, List<SyncChange> lines, CancellationToken ct)
    {
        var latest = lines.GroupBy(c => (c.EntityType, c.SyncId)).Select(g => g.MaxBy(c => c.Id)!).OrderBy(c => c.Id).ToList();
        var rows = new Dictionary<(string, Guid), SyncRow>();
        using (var scope = _scopes.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenant>().Set(rid, s.Slug);
            var store = new EfSyncStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            foreach (var g in latest.Where(c => c.Op == SyncOp.Upsert).GroupBy(c => c.EntityType))
                if (await store.Load(g.Key, rid, g.Select(c => c.SyncId).ToList(), ct) is { } loaded)
                    foreach (var (id, row) in loaded) rows[(g.Key, id)] = row;
        }
        var list = new List<object>();
        foreach (var c in latest)
        {
            var changeId = ChangeId(s.InstallId, c.Id);
            if (c.Op == SyncOp.Upsert && rows.TryGetValue((c.EntityType, c.SyncId), out var row))
                list.Add(new { changeId, type = c.EntityType, syncId = c.SyncId, op = SyncOp.Upsert, changedAt = row.LastChangedAt ?? c.ChangedAt, data = row.Data });
            else
                list.Add(new { changeId, type = c.EntityType, syncId = c.SyncId, op = SyncOp.Delete, changedAt = c.ChangedAt, data = (object?)null });
        }
        return list;
    }

    private static IEnumerable<List<object>> Batches(List<object> changes)
    {
        var batch = new List<object>();
        var size = 0;
        foreach (var c in changes)
        {
            var n = JsonSerializer.Serialize(c, Web).Length;
            if (batch.Count > 0 && (batch.Count >= 400 || size + n > 6_000_000)) { yield return batch; batch = new(); size = 0; }
            batch.Add(c);
            size += n;
        }
        if (batch.Count > 0) yield return batch;
    }

    private async Task PullAll(HttpClient http, PcSyncState s, int rid, bool takeEverything, CancellationToken ct)
    {
        for (var round = 0; round < 200; round++)
        {
            var page = (await (await Send(() => http.GetAsync($"api/sync/pull?since={s.PulledUpTo}&limit=500", ct)))
                .Content.ReadFromJsonAsync<PullPage>(Web, ct))!;
            if (page.Changes.Count > 0)
            {
                var incoming = page.Changes.Select(c => new SyncIncoming(c.Seq, c.Type, c.SyncId, c.Op, c.LastChangedAt ?? c.ChangedAt, c.Data)).ToList();
                Report(await _applier.Apply(rid, incoming, new SyncApplyOptions(SyncApplySide.OnPc, SyncChange.FromServer, takeEverything), ct), "pull");
                if (page.Changes.Any(c => c.Type is "MenuItem" or "Restaurant")) await FetchFiles(http, rid, ct);
            }
            if (page.Next > s.PulledUpTo) { s.PulledUpTo = page.Next; Save(s); }
            if (!page.More) return;
        }
    }

    private void Report(IReadOnlyList<SyncApplied> results, string what)
    {
        foreach (var r in results.Where(r => r.Status == SyncInboxStatus.Failed).Take(5))
            _log.LogWarning("Sync {What}: {Type} {Id} not applied: {Note}", what, r.Type, r.SyncId, r.Note);
    }

    // ---------------------------------------------------------------- photos

    private async Task UploadFiles(HttpClient http, List<object> changes, CancellationToken ct)
    {
        var ids = new HashSet<string>();
        foreach (var c in changes)
        {
            var json = JsonSerializer.SerializeToElement(c, Web);
            if (!json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
            foreach (var field in new[] { "ImageUrl", "LogoUrl" })
                if (data.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && FileUrl.Match(v.GetString()!) is { Success: true } m)
                    ids.Add(m.Groups[1].Value);
        }
        if (ids.Count == 0) return;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var id in ids)
        {
            using (var there = await http.GetAsync($"api/files/{id}", HttpCompletionOption.ResponseHeadersRead, ct))
                if (there.IsSuccessStatusCode) continue;   // the server has it already
            var file = await db.Set<StoredFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (file is null) continue;
            var body = new ByteArrayContent(file.Data);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            await http.PutAsync($"api/sync/files/{id}", body, ct);
        }
    }

    private async Task FetchFiles(HttpClient http, int rid, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var urls = await db.Set<Domain.Menu.MenuItem>().IgnoreQueryFilters().Where(m => m.ImageUrl != null && m.ImageUrl != "").Select(m => m.ImageUrl!).ToListAsync(ct);
        urls.AddRange(await db.Restaurants.IgnoreQueryFilters().Where(r => r.LogoUrl != "").Select(r => r.LogoUrl).ToListAsync(ct));
        var wanted = urls.Select(u => FileUrl.Match(u)).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (wanted.Count == 0) return;
        var have = (await db.Set<StoredFile>().Where(f => wanted.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();
        foreach (var id in wanted.Where(w => !have.Contains(w)))
        {
            var res = await http.GetAsync($"api/files/{id}", ct);
            if (!res.IsSuccessStatusCode) continue;
            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is 0 or > 5 * 1024 * 1024) continue;
            db.Set<StoredFile>().Add(new StoredFile { Id = id, ContentType = res.Content.Headers.ContentType?.MediaType ?? "image/jpeg", Data = bytes, RestaurantId = rid });
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- housekeeping

    private DateTime _prunedAt;

    /// <summary>Lines already sent (or applied from the server) older than three days aren't needed any more.</summary>
    private async Task Prune(PcSyncState s, CancellationToken ct)
    {
        if (DateTime.UtcNow - _prunedAt < TimeSpan.FromHours(1)) return;
        _prunedAt = DateTime.UtcNow;
        using var scope = _scopes.CreateScope();
        var old = DateTime.UtcNow.AddDays(-3);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SyncChanges.IgnoreQueryFilters()
            .Where(c => c.Id <= s.PushedUpTo && c.ChangedAt < old).ExecuteDeleteAsync(ct);
    }

    private HttpClient Client(PcSyncState s)
    {
        var http = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
        http.BaseAddress = new Uri(s.Server.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.Add(Endpoints.SyncEndpoints.DeviceKeyHeader, s.Key);
        return http;
    }

    private static async Task<HttpResponseMessage> Send(Func<Task<HttpResponseMessage>> call)
    {
        var res = await call();
        if (res.IsSuccessStatusCode) return res;
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new PcKeyRefusedException();
        throw new InvalidOperationException($"The online server said {(int)res.StatusCode}: {await Detail(res, default)}");
    }

    private static async Task<string> Detail(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : body[..Math.Min(200, body.Length)];
        }
        catch { return res.ReasonPhrase ?? ""; }
    }

    /// <summary>The same id every time this PC sends this line again, so the server keeps it once.</summary>
    private static Guid ChangeId(Guid install, long line)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{install:N}:{line}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string Words(string type) => Regex.Replace(type, "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant() switch
    {
        var w when w.EndsWith('y') => w[..^1] + "ies",
        var w when w.EndsWith('s') => w,
        var w => w + "s",
    };

    private PcSyncState? Load()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<PcSyncState>(File.ReadAllText(StateFile), Web) : null; }
        catch (Exception ex) { _log.LogError(ex, "sync.json can't be read"); return null; }
    }

    private void Save(PcSyncState s)
    {
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions(Web) { WriteIndented = true }));
        File.Move(tmp, StateFile, overwrite: true);
    }

    private sealed record SnapshotRow(Guid SyncId, DateTime? LastChangedAt, JsonElement Data);
    private sealed record SnapshotPage(string Type, List<SnapshotRow> Items, int? Next);
    private sealed record PulledRow(long Seq, string Type, Guid SyncId, string Op, DateTime ChangedAt, DateTime? LastChangedAt, JsonElement? Data);
    private sealed record PullPage(List<PulledRow> Changes, long Next, bool More);
}

public sealed record PcConnectRequest(string? Server, string? Key);

public static class PcSyncEndpoints
{
    public static void MapPcSync(this WebApplication app)
    {
        // How the online copy stands. Open to the connect screen (before any restaurant exists) and staff screens alike;
        // it never shows the PC key.
        app.MapGet("/api/pc/sync", async (PcSync sync, CancellationToken ct) => Results.Ok(await sync.Status(ct)));
        // A PC with no restaurant yet connects (Connect checks); the licence gate has already checked the licence.
        // A connected PC takes a new key only from staff who manage settings.
        app.MapPost("/api/pc/sync/connect", async (PcConnectRequest body, PcSync sync, HttpContext ctx,
            IAuthorizationService authz, CancellationToken ct) =>
        {
            if (sync.Connected && !(await authz.AuthorizeAsync(ctx.User, QrDine.Api.Auth.Policies.CanManageSettings)).Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not allowed",
                    detail: "Sign in as the owner or a manager to enter a new PC key.");
            await sync.Connect(body.Server, body.Key, ct);
            return Results.Ok(await sync.Status(ct));
        }).RequireRateLimiting("login");
        app.MapPost("/api/pc/sync/now", async (PcSync sync, CancellationToken ct) =>
        {
            sync.Wake();
            return Results.Ok(await sync.Status(ct));
        }).RequireAuthorization(QrDine.Api.Auth.Policies.CanManageSettings);
    }
}
