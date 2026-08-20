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

        // Auth: password hashing + JWT issuing.
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

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
        var simulate = config.GetValue("Fiscal:Simulate", true);
        if (simulate)
        {
            services.AddScoped<IFiscalService, SimulatedFiscalService>();
        }
        else
        {
            // Real FBR transport. FBR's gateway presents a certificate their spec has
            // clients bypass-validate, so we relax validation for THIS client only.
            // Short timeout: at settle time this call is on the cashier's critical path,
            // so an offline FBR must fail fast → invoice goes Pending, receipt prints now.
            services.AddHttpClient<IFiscalService, FbrFiscalService>(c => c.Timeout = TimeSpan.FromSeconds(8))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                });
        }

        return services;
    }
}
