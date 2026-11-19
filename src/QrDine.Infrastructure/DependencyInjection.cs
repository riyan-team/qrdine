using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Infrastructure.Fiscal;
using QrDine.Infrastructure.Identity;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using QrDine.Infrastructure.Realtime;

namespace QrDine.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires persistence, tenancy and realtime. The database provider is chosen
    /// by config: "Sqlite" for local dev (zero install), "Postgres" for the cloud.
    /// The rest of the app never knows the difference.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var conn = config.GetConnectionString("Default")
                   ?? "Data Source=qrdine.db";

        services.AddDbContext<AppDbContext>(opt =>
        {
            // Split multi-collection includes (e.g. menu → variants + modifier groups)
            // into separate queries to avoid a cartesian explosion.
            if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
                opt.UseNpgsql(conn, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            else
                opt.UseSqlite(conn, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        });

        // Application depends on the abstraction; hand it the EF context.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Sync with a restaurant's own PC: off unless Sync:Enabled=true (see SyncSettings).
        services.AddSingleton(new SyncSettings(
            config.GetValue("Sync:Enabled", false),
            config.GetValue("Sync:SettleSeconds", 2)));
        services.AddScoped<Application.Sync.ISyncStore, Sync.EfSyncStore>();
        services.AddSingleton<Application.Sync.ISyncApplier, Sync.SyncApplier>();

        // One tenant holder per request.
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());

        services.AddScoped<IOrderNotifier, SignalROrderNotifier>();
        services.AddScoped<IPrintDispatcher, SignalRPrintDispatcher>();

        // File storage: "Db" (default) keeps uploads in the database so they survive
        // redeploys on an ephemeral-disk host; "Local" writes to wwwroot/uploads.
        var storageProvider = config["Storage:Provider"] ?? "Db";
        if (storageProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, Storage.LocalFileStorage>();
        else
            services.AddScoped<IFileStorage, Storage.DbFileStorage>();

        // Menu photo import via Gemini (free tier). Works only once Gemini:ApiKey is set;
        // the CSV import needs nothing. Each try has its own time limit inside the extractor
        // (retries and fallback models fit in 90 s); this is only the outer guard.
        services.AddHttpClient<Application.Menu.IMenuVisionExtractor, Menu.GeminiMenuExtractor>(
            c => c.Timeout = TimeSpan.FromSeconds(100));

        // Auth: password hashing + JWT issuing.
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        // The live feed's member list (the minute-by-minute check that uses it is started next to SignalR in Program).
        services.AddSingleton<Realtime.LiveConnections>();

        // Payments — pick the gateway by config (Mock for dev, JazzCash when creds set).
        services.Configure<Payments.PaymentOptions>(config.GetSection("Payments"));
        var payProvider = config["Payments:Provider"] ?? "Mock";
        if (payProvider.Equals("JazzCash", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IPaymentGateway, Payments.JazzCashPaymentGateway>();
        else
            services.AddScoped<IPaymentGateway, Payments.MockPaymentGateway>();

        // Fiscal (FBR) — whether a restaurant files, and Local vs Cloud, is decided
        // per-tenant at runtime (see FiscalSyncService). Config only chooses simulate
        // (dev/demo) vs the real HTTP transport.
        services.Configure<FiscalOptions>(config.GetSection("Fiscal"));
        services.AddSingleton<IFiscalCapabilities, FiscalCapabilities>();
        // One transport for every branch: Practice is answered in the app, Test and Live go to FBR or PRA.
        // FBR's own servers have long shown certificates their spec tells clients to accept anyway; that exception
        // (Fiscal:AllowInvalidCertificate, on by default) is for FBR's hosts and the PC's FBR software only, never
        // for PRA or a branch's own test address. Short timeout: at settle this is on the cashier's critical path,
        // so an unreachable office must fail fast and leave the bill waiting.
        var allowInvalidCert = config.GetValue("Fiscal:AllowInvalidCertificate", true);
        services.AddHttpClient<IFiscalService, FbrFiscalService>(c => c.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (req, _, _, errors) =>
                    errors == System.Net.Security.SslPolicyErrors.None
                    || (allowInvalidCert && req.RequestUri?.Host is "esp.fbr.gov.pk" or "gw.fbr.gov.pk" or "localhost" or "127.0.0.1"),
            });
        // Every few minutes, bills that couldn't reach the tax office are sent again.
        services.AddHostedService<FiscalAutoRetryService>();

        return services;
    }
}
