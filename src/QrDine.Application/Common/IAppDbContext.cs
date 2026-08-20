using Microsoft.EntityFrameworkCore;
using QrDine.Domain.Billing;
using QrDine.Domain.Identity;
using QrDine.Domain.Inventory;
using QrDine.Domain.Menu;
using QrDine.Domain.Ordering;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Tables;

namespace QrDine.Application.Common;

/// <summary>
/// The persistence contract the Application layer depends on. Infrastructure's
/// EF Core AppDbContext implements it. Application never references EF Core's
/// concrete DbContext, so the storage engine stays swappable.
/// </summary>
public interface IAppDbContext
{
    DbSet<Restaurant> Restaurants { get; }
    DbSet<Branch> Branches { get; }
    DbSet<DiningTable> Tables { get; }
    DbSet<TableSession> Sessions { get; }
    DbSet<MenuCategory> Categories { get; }
    DbSet<MenuItem> MenuItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<StaffMember> Staff { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Payment> Payments { get; }
    DbSet<StockItem> Stock { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<PlatformAdmin> PlatformAdmins { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<QrDine.Domain.Subscriptions.Plan> Plans { get; }
    DbSet<QrDine.Domain.Subscriptions.SubscriptionPayment> SubscriptionPayments { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
