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

// ---- composition root: each layer wires its own services ----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQrDineAuth(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// ---- create/upgrade the database and seed the demo restaurant + staff ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    // SQLite (dev) ships migrations; the cloud Postgres DB is built straight from
    // the model with EnsureCreated (no Postgres-specific migrations to maintain).
    var dbProvider = app.Configuration["Database:Provider"] ?? "Sqlite";
    if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();

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

app.UseSerilogRequestLogging();                     // one tidy log line per request
app.UseCors();
app.UseMiddleware<ExceptionHandlingMiddleware>();   // outermost: catches everything below
app.UseAuthentication();                            // populate ctx.User before tenant check
app.UseMiddleware<TenantResolutionMiddleware>();    // resolve tenant + block cross-tenant tokens
app.UseAuthorization();                              // enforce role policies on endpoints

app.MapOnboarding();
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
