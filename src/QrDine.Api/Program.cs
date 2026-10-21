using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QrDine.Api.Auth;
using QrDine.Api.Endpoints;
using QrDine.Api.Middleware;
using QrDine.Application;
using QrDine.Application.Common;
using QrDine.Infrastructure;
using QrDine.Infrastructure.Persistence;
using QrDine.Infrastructure.Realtime;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Render / most PaaS inject the port to listen on via $PORT.
var listenPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(listenPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");

// Postgres (Npgsql): treat our UTC/unspecified DateTimes leniently so date filters
// and timestamp columns don't throw on DateTimeKind. Harmless for SQLite.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// wwwroot must exist before the app is built so the static-file provider is valid.
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads"));

// ---- structured logging ----
builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Fail fast in production on a weak/default JWT secret. A guessable secret lets an
// attacker forge a platform token and take over the whole control plane, so this is
// a hard startup gate (Development keeps the convenient dev secret).
if (!builder.Environment.IsDevelopment())
{
    var secret = builder.Configuration["Jwt:Secret"] ?? "";
    if (secret.Length < 32 || secret.Contains("dev-only", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "Jwt:Secret must be a random value of at least 32 characters in production. " +
            "Set Jwt__Secret (e.g. `openssl rand -base64 48`).");
}

// ---- composition root: each layer wires its own services ----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQrDineAuth(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

// CORS: the SPA is served from the API's own origin, so cross-origin isn't needed
// in production. If Cors:Origins is set (comma-separated), allow exactly those;
// otherwise reflect any origin but WITHOUT credentials (we use Bearer headers, not
// cookies), which is safe — a page can't read a response it isn't allowed to.
var corsOrigins = (builder.Configuration["Cors:Origins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    p.AllowAnyHeader().AllowAnyMethod();
    if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins).AllowCredentials();
    else p.SetIsOriginAllowed(_ => true);
}));

// Trust the platform proxy's forwarded headers so rate limiting / logging see the
// real client IP (Render terminates TLS and sets X-Forwarded-For / -Proto).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();   // accept the single PaaS proxy hop
    o.KnownProxies.Clear();
});

// Server-side rate limiting per client IP to blunt brute-force and abuse. Limits are
// configurable via the "RateLimits" config section (see appsettings.json); the values
// below are the fallbacks. Each named policy is applied to its endpoints with
// .RequireRateLimiting(name). Rejections return 429 with a friendly problem+json body.
string ClientKey(HttpContext ctx) =>
    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

FixedWindowRateLimiterOptions Window(int permit, int seconds) =>
    new() { PermitLimit = permit, Window = TimeSpan.FromSeconds(seconds), QueueLimit = 0 };

int Limit(string name, int fallback) =>
    builder.Configuration.GetValue($"RateLimits:{name}:PermitLimit", fallback);
int WindowSecs(string name, int fallback) =>
    builder.Configuration.GetValue($"RateLimits:{name}:WindowSeconds", fallback);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Sign-in / password endpoints: tight, to slow credential stuffing.
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => Window(Limit("login", 10), WindowSecs("login", 60))));
    // New-restaurant sign-up: very tight — few legitimate attempts per IP.
    o.AddPolicy("register", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => Window(Limit("register", 5), WindowSecs("register", 3600))));
    // Order placement (guest + waiter): generous but bounded against spam.
    o.AddPolicy("orders", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => Window(Limit("orders", 40), WindowSecs("orders", 60))));
    // Public guest endpoints (table session issue, feedback): bounded per IP.
    o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => Window(Limit("public", 60), WindowSecs("public", 60))));

    // Friendly rejection body (never a bare 429). Don't reveal whether an account exists.
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = "Too many attempts",
            status = StatusCodes.Status429TooManyRequests,
            detail = "You've tried a few times in quick succession. Please wait a moment and try again."
        }, ct);
    };
});

var app = builder.Build();

// ---- create/upgrade the database and seed the demo restaurant + staff ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    // Cloud Postgres applies EF migrations, so every schema change deploys itself.
    // Local dev (SQLite) is a throwaway DB built straight from the model — delete
    // qrdine.db to reset — so dev never needs to maintain migrations.
    var dbProvider = app.Configuration["Database:Provider"] ?? "Sqlite";
    if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        // One-time recovery: if the DB was built the old way (tables but no migration
        // history), Migrate() collides ("relation already exists"). Setting
        // Database__ResetSchema=true wipes the public schema so migrations rebuild it
        // cleanly. Set it, deploy once, then REMOVE it. Destructive — deletes all data.
        if (app.Configuration.GetValue("Database:ResetSchema", false))
        {
            Log.Warning("Database:ResetSchema=true — dropping and recreating the public schema.");
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
        }
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    // Platform operator bootstrap. In production set PlatformAdmin__Username /
    // PlatformAdmin__Password as environment variables. In Development we fall back
    // to a known dev login so the console is reachable locally.
    var padminUser = app.Configuration["PlatformAdmin:Username"];
    var padminPass = app.Configuration["PlatformAdmin:Password"];
    if (string.IsNullOrWhiteSpace(padminPass) && app.Environment.IsDevelopment())
    {
        padminUser = "superadmin";
        padminPass = "super123";
    }
    await DbSeeder.SeedAsync(db, hasher, padminUser, padminPass);
}

// Serve uploaded menu photos from wwwroot/uploads.
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads"));
app.UseStaticFiles();

app.UseForwardedHeaders();                          // real client IP behind the proxy
app.UseSerilogRequestLogging();                     // one tidy log line per request
app.UseCors();
app.UseRateLimiter();                               // throttle login endpoints
app.UseMiddleware<ExceptionHandlingMiddleware>();   // outermost: catches everything below
app.UseAuthentication();                            // populate ctx.User before tenant check
app.UseMiddleware<TenantResolutionMiddleware>();    // resolve tenant + block cross-tenant tokens
app.UseAuthorization();                              // enforce role policies on endpoints

app.MapOnboarding();
app.MapFiles();
app.MapFeedback();
app.MapLoyalty();
app.MapPayments();
app.MapPlatform();
app.MapAuth();
app.MapMenu();
app.MapMenuAdmin();
app.MapAdminSettings();
app.MapInventory();
app.MapOrders();
app.MapStaff();
app.MapBilling();
app.MapFiscal();
app.MapHub<OrderHub>("/hub/orders");
app.MapHealthChecks("/health");

// SPA: everything that isn't an API/hub route serves the built React app.
app.MapFallbackToFile("index.html");

app.Run();
