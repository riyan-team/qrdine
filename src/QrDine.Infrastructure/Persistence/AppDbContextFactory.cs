using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QrDine.Application.Common;

namespace QrDine.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (migrations). It targets Npgsql so
/// the generated migrations are Postgres-native — that's the deployed database.
/// (Local dev uses SQLite via EnsureCreated and never runs these migrations.)
/// The connection string is a placeholder; `migrations add` only reads the model
/// and never connects.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // MUST match Program.cs, or the migration snapshot (timestamptz) won't match
        // the runtime model (timestamp) and Migrate() throws PendingModelChangesWarning.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=qrdine;Username=postgres;Password=postgres")
            .Options;
        return new AppDbContext(options, new DesignTimeTenant());
    }

    private class DesignTimeTenant : ICurrentTenant
    {
        public int? RestaurantId => null;
        public string? Slug => null;
        public bool IsResolved => false;
        public void Set(int restaurantId, string slug) { }
    }
}
