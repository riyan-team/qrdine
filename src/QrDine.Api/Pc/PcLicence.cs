using Microsoft.EntityFrameworkCore;
using QrDine.Application.Pc;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Api.Pc;

/// <summary>
/// The PC edition's licence: read from licence.key, checked against this computer's code, the date and the latest
/// time the PC has already run at (so moving the clock back doesn't extend a licence). Checked again every few
/// minutes, so a licence that ends while the app runs takes effect without a restart.
/// </summary>
public sealed class PcLicence
{
    /// <summary>The seller's public key. Its private half stays with the seller (the licence tool), never in the code.</summary>
    public const string SellerPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEtTBkuCJ1YUvbI24A/1+Tz2afI+b4NXSFC1swYJKlAJitq93RTF4cR/Rf9ejv9XLF4mWlU5tOyNJzAJLU5MV4nw==";

    private readonly PcPaths _paths;
    private readonly LicenceVerifier _verifier;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private LicenceStatus _status;
    private DateTime _checkedAt;
    private DateTime _lastSeen;

    public string MachineCode { get; }

    public PcLicence(PcPaths paths, LicenceVerifier verifier, string machineCode, TimeProvider? clock = null)
    {
        _paths = paths; _verifier = verifier; MachineCode = MachineCodeText.Pretty(machineCode); _clock = clock ?? TimeProvider.System;
        _lastSeen = ReadLastSeen();
        _status = Check();
    }

    /// <summary>The licence now (re-checked every 5 minutes).</summary>
    public LicenceStatus Status
    {
        get
        {
            lock (_gate)
            {
                if (Now() - _checkedAt > TimeSpan.FromMinutes(5)) _status = Check();
                return _status;
            }
        }
    }

    /// <summary>The newest record in the database counts as a time this PC already ran at.</summary>
    public void SawRecordsUpTo(DateTime utc)
    {
        lock (_gate)
        {
            if (utc > _lastSeen) _lastSeen = utc;
            _status = Check();
        }
    }

    /// <summary>Saves the key when it is a working licence for this PC. Otherwise nothing changes.</summary>
    public LicenceStatus Activate(string key)
    {
        lock (_gate)
        {
            var s = _verifier.Evaluate(key, MachineCode, Now(), _lastSeen);
            if (!s.CanWork) return s;
            File.WriteAllText(_paths.LicenceFile, key.Trim());
            _status = s;
            _checkedAt = Now();
            return s;
        }
    }

    private LicenceStatus Check()
    {
        var now = Now();
        _checkedAt = now;
        var key = File.Exists(_paths.LicenceFile) ? File.ReadAllText(_paths.LicenceFile) : null;
        var s = _verifier.Evaluate(key, MachineCode, now, _lastSeen);
        if (now > _lastSeen && s.State != LicenceState.ClockBack)
        {
            _lastSeen = now;
            try { File.WriteAllText(_paths.ClockFile, now.Ticks.ToString()); } catch (IOException) { /* next time */ }
        }
        return s;
    }

    private DateTime ReadLastSeen() =>
        File.Exists(_paths.ClockFile) && long.TryParse(File.ReadAllText(_paths.ClockFile).Trim(), out var t) && t > 0 && t < DateTime.MaxValue.Ticks
            ? new DateTime(t, DateTimeKind.Utc) : DateTime.MinValue;

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}

/// <summary>
/// Applies a working licence to the restaurant on this PC: the features it pays for are switched on, the rest off.
/// Runs at start and after a new licence is entered.
/// </summary>
public static class PcLicenceApplier
{
    public static async Task Apply(AppDbContext db, PcLicence licence, ILogger log, CancellationToken ct = default)
    {
        var newest = await db.Invoices.IgnoreQueryFilters().MaxAsync(i => (DateTime?)i.IssuedAt, ct);
        if (newest is DateTime n) licence.SawRecordsUpTo(DateTime.SpecifyKind(n, DateTimeKind.Utc));

        var s = licence.Status;
        if (!s.CanWork || s.Licence is null) return;
        var f = s.Licence.Features.Select(x => x.ToLowerInvariant()).ToHashSet();
        foreach (var r in await db.Restaurants.IgnoreQueryFilters().ToListAsync(ct))
        {
            r.FeatureOrdering = f.Contains("ordering");
            r.FeatureKitchen = f.Contains("kitchen");
            r.FeatureWaiter = f.Contains("waiter");
            r.FeatureBilling = f.Contains("billing");
            r.FeatureInventory = f.Contains("inventory");
            r.FeatureReports = f.Contains("reports");
            r.FeatureFbr = f.Contains("fbr");
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("Licence {Id} for {Restaurant}: {State}, features {Features}", s.Licence.Id, s.Licence.Restaurant, s.State, string.Join(",", f));
    }
}

/// <summary>
/// Without a working licence the PC edition still lets staff sign in and read their records (an ended licence
/// never locks a restaurant out of its own data), but nothing new can be made. With no licence at all, only the
/// licence screen works. The PC edition holds one restaurant.
/// </summary>
public sealed class LicenceGateMiddleware(RequestDelegate next, PcLicence licence, ILogger<LicenceGateMiddleware> log, PcSync? sync = null)
{
    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        var path = ctx.Request.Path;
        var api = path.StartsWithSegments("/api") || path.StartsWithSegments("/hub");
        if (!api || path.StartsWithSegments("/api/licence")) { await next(ctx); return; }

        // Connecting to the online restaurant: nothing opens until the download is complete and this PC runs the counter.
        if (sync is { Downloading: true } && !path.StartsWithSegments("/api/pc/sync"))
        {
            await Refuse(ctx, StatusCodes.Status503ServiceUnavailable, "Downloading", "This PC is downloading its restaurant from online. It opens when that is done.");
            return;
        }

        var signUp = HttpMethods.IsPost(ctx.Request.Method) && path.StartsWithSegments("/api/onboard");
        if (signUp && await db.Restaurants.IgnoreQueryFilters().AnyAsync(ctx.RequestAborted))
        {
            await Refuse(ctx, 403, "One restaurant per PC", "This PC already has its restaurant. Sign in instead.");
            return;
        }

        var s = licence.Status;
        if (s.CanWork)
        {
            await next(ctx);
            // The restaurant was made after the licence was entered: it gets the licence's features, not the plan's.
            if (signUp && ctx.Response.StatusCode == StatusCodes.Status200OK)
                await PcLicenceApplier.Apply(db, licence, log, ctx.RequestAborted);
            return;
        }
        var m = ctx.Request.Method;
        var reading = HttpMethods.IsGet(m) || HttpMethods.IsHead(m) || HttpMethods.IsOptions(m) || path.StartsWithSegments("/hub")
                      || path.Value!.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase);
        if (s.CanRead && reading) { await next(ctx); return; }
        await Refuse(ctx, StatusCodes.Status402PaymentRequired, "Licence needed", s.Message);
    }

    private static async Task Refuse(HttpContext ctx, int status, string title, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new { type = "licence", title, status, detail });
    }
}

public sealed record LicenceRequest(string? Key);

public static class LicenceEndpoints
{
    public static object View(PcLicence l, bool hasRestaurant)
    {
        var s = l.Status;
        return new
        {
            edition = "pc",
            hasRestaurant,
            state = s.State.ToString(),
            s.CanWork,
            s.CanRead,
            machineCode = l.MachineCode,
            restaurant = s.Licence?.Restaurant,
            expires = s.Licence?.Expires,
            s.DaysLeft,
            s.Message,
            features = s.Licence?.Features ?? [],
        };
    }

    public static void MapLicence(this WebApplication app)
    {
        app.MapGet("/api/licence", async (PcLicence l, AppDbContext db) =>
            Results.Ok(View(l, await db.Restaurants.IgnoreQueryFilters().AnyAsync())));
        app.MapPost("/api/licence", async (LicenceRequest body, PcLicence l, AppDbContext db, ILoggerFactory logs) =>
        {
            var s = l.Activate(body.Key ?? "");
            if (!s.CanWork) return Results.Problem(detail: s.Message, statusCode: 400, title: "Licence not accepted");
            await PcLicenceApplier.Apply(db, l, logs.CreateLogger("Licence"));
            return Results.Ok(View(l, await db.Restaurants.IgnoreQueryFilters().AnyAsync()));
        }).RequireRateLimiting("login");
    }
}
