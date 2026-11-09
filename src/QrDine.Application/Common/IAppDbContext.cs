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
    DbSet<QrDine.Domain.Files.StoredFile> StoredFiles { get; }
    DbSet<QrDine.Domain.Feedback.Feedback> Feedbacks { get; }
    DbSet<QrDine.Domain.Loyalty.LoyaltyProgram> LoyaltyPrograms { get; }
    DbSet<QrDine.Domain.Loyalty.LoyaltyMember> LoyaltyMembers { get; }
    DbSet<QrDine.Domain.Loyalty.LoyaltyReward> LoyaltyRewards { get; }
    DbSet<QrDine.Domain.Loyalty.LoyaltyTransaction> LoyaltyTransactions { get; }
    DbSet<QrDine.Domain.Loyalty.LoyaltyRedemption> LoyaltyRedemptions { get; }
    DbSet<QrDine.Domain.Purchasing.Supplier> Suppliers { get; }
    DbSet<QrDine.Domain.Purchasing.SupplierEntry> SupplierEntries { get; }
    DbSet<QrDine.Domain.Expenses.Expense> Expenses { get; }
    DbSet<QrDine.Domain.Customers.Customer> Customers { get; }
    DbSet<QrDine.Domain.Customers.CustomerEntry> CustomerEntries { get; }
    DbSet<QrDine.Domain.Staffing.Employee> Employees { get; }
    DbSet<QrDine.Domain.Staffing.EmployeeEntry> EmployeeEntries { get; }
    DbSet<QrDine.Domain.Staffing.AttendanceMark> AttendanceMarks { get; }
    DbSet<QrDine.Domain.Ordering.OrderVoid> OrderVoids { get; }
    DbSet<QrDine.Domain.Ordering.ServiceRequest> ServiceRequests { get; }
    DbSet<QrDine.Domain.Billing.DayClosing> DayClosings { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
