using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QrDine.Application.Common;

namespace QrDine.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (migrations). It builds the
/// context against SQLite with an unresolved tenant — enough to read the model.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=qrdine.db")
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
