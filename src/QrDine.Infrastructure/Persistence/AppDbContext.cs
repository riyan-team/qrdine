using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Inventory;
using QrDine.Domain.Menu;
using QrDine.Domain.Ordering;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Tables;

namespace QrDine.Infrastructure.Persistence;

/// <summary>
/// The EF Core context. It implements the Application's IAppDbContext so the
/// Application layer never sees EF's concrete type. A global query filter scopes
/// every tenant-owned entity to the current restaurant, and SaveChanges stamps
/// RestaurantId on new rows automatically.
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ICurrentTenant _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant) : base(options)
    {
        _tenant = tenant;
    }

    // Read at query time (not construction), so middleware can resolve the tenant
    // after this context is created and the filter still scopes correctly.
    private int? TenantId => _tenant.RestaurantId;

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<DiningTable> Tables => Set<DiningTable>();
    public DbSet<TableSession> Sessions => Set<TableSession>();
    public DbSet<MenuCategory> Categories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<StaffMember> Staff => Set<StaffMember>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Domain.Subscriptions.Plan> Plans => Set<Domain.Subscriptions.Plan>();
    public DbSet<Domain.Subscriptions.SubscriptionPayment> SubscriptionPayments => Set<Domain.Subscriptions.SubscriptionPayment>();
    public DbSet<Domain.Files.StoredFile> StoredFiles => Set<Domain.Files.StoredFile>();
    public DbSet<Domain.Feedback.Feedback> Feedbacks => Set<Domain.Feedback.Feedback>();
    public DbSet<Domain.Loyalty.LoyaltyProgram> LoyaltyPrograms => Set<Domain.Loyalty.LoyaltyProgram>();
    public DbSet<Domain.Loyalty.LoyaltyMember> LoyaltyMembers => Set<Domain.Loyalty.LoyaltyMember>();
    public DbSet<Domain.Loyalty.LoyaltyReward> LoyaltyRewards => Set<Domain.Loyalty.LoyaltyReward>();
    public DbSet<Domain.Loyalty.LoyaltyTransaction> LoyaltyTransactions => Set<Domain.Loyalty.LoyaltyTransaction>();
    public DbSet<Domain.Loyalty.LoyaltyRedemption> LoyaltyRedemptions => Set<Domain.Loyalty.LoyaltyRedemption>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Restaurant>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Plan).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<PlatformAdmin>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(60);
        });

        b.Entity<Domain.Subscriptions.Plan>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.PricePkr).HasPrecision(10, 2);
            e.Property(x => x.SetupFeePkr).HasPrecision(10, 2);
        });

        b.Entity<Domain.Files.StoredFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.ContentType).HasMaxLength(60);
        });

        b.Entity<Domain.Feedback.Feedback>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Property(x => x.Comment).HasMaxLength(500);
            e.Property(x => x.TableCode).HasMaxLength(20);
            e.HasIndex(x => new { x.RestaurantId, x.CreatedAt });
        });

        // ---- Loyalty & reviews (all tenant-owned) ----
        b.Entity<Domain.Loyalty.LoyaltyProgram>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.HasIndex(x => x.RestaurantId).IsUnique();     // one programme per restaurant
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Tagline).HasMaxLength(160);
            e.Property(x => x.PerAmount).HasPrecision(12, 2);
            e.Property(x => x.MinBillAmount).HasPrecision(12, 2);
            e.Property(x => x.GoogleReviewUrl).HasMaxLength(400);
            e.Property(x => x.GoogleMapsUrl).HasMaxLength(400);
        });

        b.Entity<Domain.Loyalty.LoyaltyMember>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.HasIndex(x => new { x.RestaurantId, x.Phone }).IsUnique();
            e.HasIndex(x => new { x.RestaurantId, x.Token });
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Token).HasMaxLength(40);
            e.Property(x => x.LifetimeSpend).HasPrecision(14, 2);
        });

        b.Entity<Domain.Loyalty.LoyaltyReward>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Property(x => x.Title).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Emoji).HasMaxLength(16);
        });

        b.Entity<Domain.Loyalty.LoyaltyTransaction>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.HasIndex(x => new { x.RestaurantId, x.MemberId, x.CreatedAt });
            e.HasIndex(x => new { x.RestaurantId, x.TableSessionId });
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.BillAmount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.CreatedBy).HasMaxLength(60);
        });

        b.Entity<Domain.Loyalty.LoyaltyRedemption>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.HasIndex(x => new { x.RestaurantId, x.Status, x.RequestedAt });
            e.HasIndex(x => new { x.RestaurantId, x.MemberId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RewardTitle).HasMaxLength(60);
            e.Property(x => x.RewardEmoji).HasMaxLength(16);
            e.Property(x => x.Code).HasMaxLength(10);
            e.Property(x => x.TableCode).HasMaxLength(20);
            e.Property(x => x.DecidedBy).HasMaxLength(60);
            e.Property(x => x.Note).HasMaxLength(200);
        });

        b.Entity<Domain.Subscriptions.SubscriptionPayment>(e =>
        {
            e.Property(x => x.AmountPkr).HasPrecision(12, 2);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.RecordedBy).HasMaxLength(60);
            e.HasIndex(x => x.RestaurantId);
        });

        b.Entity<RolePermission>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Permission).HasMaxLength(40);
            e.HasIndex(x => new { x.RestaurantId, x.Role, x.Permission }).IsUnique();
        });

        b.Entity<Branch>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Address).HasMaxLength(200);
            e.Property(x => x.FbrMode).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.FbrToken).HasMaxLength(200);
            e.Property(x => x.FbrEndpointOverride).HasMaxLength(200);
            e.Property(x => x.FbrStaticIp).HasMaxLength(45);
        });

        b.Entity<DiningTable>(e =>
        {
            e.HasIndex(x => new { x.RestaurantId, x.Code }).IsUnique();
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
        });

        b.Entity<TableSession>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            // The open-session lookups (by table id, and by table code) hit these constantly.
            e.HasIndex(x => new { x.RestaurantId, x.DiningTableId, x.IsOpen });
            e.HasIndex(x => new { x.RestaurantId, x.TableCode, x.IsOpen });
        });

        b.Entity<MenuCategory>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Ignore(x => x.Items);   // read via MenuItems directly in P1
        });

        b.Entity<MenuItem>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.DealPrice).HasPrecision(10, 2);
            e.Property(x => x.DealDescription).HasMaxLength(200);
            e.Property(x => x.DealDays).HasMaxLength(20);
            e.Property(x => x.TaxRate).HasPrecision(5, 2);
            e.Property(x => x.FurtherTaxRate).HasPrecision(5, 2);
            e.Property(x => x.PctCode).HasMaxLength(12);
            e.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.MenuItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ModifierGroups).WithOne().HasForeignKey(g => g.MenuItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Recipe).WithOne().HasForeignKey(r => r.MenuItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MenuItemVariant>(e => e.Property(x => x.Price).HasPrecision(10, 2));
        b.Entity<ModifierGroup>(e =>
            e.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.ModifierGroupId).OnDelete(DeleteBehavior.Cascade));
        b.Entity<ModifierOption>(e => e.Property(x => x.Price).HasPrecision(10, 2));

        b.Entity<Order>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Ignore(x => x.Total);
            e.Ignore(x => x.DomainEvents);
            // Indexes on the hot columns the staff board, table poll, history and
            // session lookups filter/sort by — turns per-request table scans into seeks.
            e.HasIndex(x => new { x.RestaurantId, x.Status });
            e.HasIndex(x => new { x.RestaurantId, x.TableSessionId });
            e.HasIndex(x => new { x.RestaurantId, x.CreatedAt });
            e.HasIndex(x => new { x.RestaurantId, x.TableCode });
            // Map the aggregate's private _items collection.
            var nav = e.Metadata.FindNavigation(nameof(Order.Items))!;
            nav.SetPropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OrderItem>(e =>
        {
            e.Ignore(x => x.LineTotal);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.HasMany(x => x.Modifiers).WithOne().HasForeignKey(m => m.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OrderItemModifier>(e => e.Property(x => x.Price).HasPrecision(10, 2));

        b.Entity<StaffMember>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.HasIndex(x => new { x.RestaurantId, x.Username }).IsUnique();
            e.Property(x => x.Username).HasMaxLength(60);
            e.Property(x => x.Role).HasConversion<string>();
            e.Property(x => x.FbrToken).HasMaxLength(200);
        });

        b.Entity<Restaurant>(e =>
        {
            e.Property(x => x.TaxPercent).HasPrecision(5, 2);
            e.Property(x => x.ServiceChargePercent).HasPrecision(5, 2);
            e.Property(x => x.DiscountValue).HasPrecision(10, 2);
            e.Property(x => x.TaxName).HasMaxLength(40);
            e.Property(x => x.BusinessName).HasMaxLength(150);
            e.Property(x => x.Ntn).HasMaxLength(15);
            e.Property(x => x.Strn).HasMaxLength(20);
        });

        b.Entity<Invoice>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.HasIndex(x => new { x.RestaurantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.RestaurantId, x.Usin }).IsUnique();
            e.Property(x => x.Usin).HasMaxLength(50);
            e.Property(x => x.FiscalStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FiscalProvider).HasMaxLength(20);
            e.Property(x => x.FiscalNumber).HasMaxLength(60);
            e.Property(x => x.FiscalQrPayload).HasMaxLength(512);
            e.Property(x => x.FiscalError).HasMaxLength(1000);
            e.Property(x => x.CreatedByUsername).HasMaxLength(60);
            e.Property(x => x.TaxLabel).HasMaxLength(60);
            e.Property(x => x.BuyerName).HasMaxLength(150);
            e.Property(x => x.BuyerNtn).HasMaxLength(15);
            e.Property(x => x.BuyerCnic).HasMaxLength(15);
            e.Property(x => x.BuyerPhone).HasMaxLength(20);
            foreach (var p in new[] { nameof(Invoice.Subtotal), nameof(Invoice.TaxPercent),
                nameof(Invoice.TaxAmount), nameof(Invoice.FurtherTaxAmount), nameof(Invoice.ServiceChargePercent),
                nameof(Invoice.ServiceChargeAmount), nameof(Invoice.DiscountAmount), nameof(Invoice.Total),
                nameof(Invoice.AmountTendered), nameof(Invoice.ChangeGiven) })
                e.Property(p).HasPrecision(12, 2);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InvoiceLine>(e =>
        {
            e.Ignore(x => x.LineTotal);
            e.Property(x => x.ItemCode).HasMaxLength(50);
            e.Property(x => x.PctCode).HasMaxLength(12);
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.TaxRate).HasPrecision(5, 2);
            e.Property(x => x.FurtherTaxRate).HasPrecision(5, 2);
            foreach (var p in new[] { nameof(InvoiceLine.SaleValue), nameof(InvoiceLine.DiscountAmount),
                nameof(InvoiceLine.TaxAmount), nameof(InvoiceLine.FurtherTaxAmount), nameof(InvoiceLine.TotalAmount) })
                e.Property(p).HasPrecision(12, 2);
        });

        b.Entity<Payment>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.HasIndex(x => new { x.RestaurantId, x.TableSessionId });
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Method).HasMaxLength(30);
        });

        b.Entity<StockItem>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Ignore(x => x.IsLow);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.LowThreshold).HasPrecision(12, 3);
        });

        b.Entity<StockMovement>(e =>
        {
            e.HasQueryFilter(x => x.RestaurantId == TenantId);
            e.Property(x => x.Delta).HasPrecision(12, 3);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<RecipeLine>(e => e.Property(x => x.QtyPerServing).HasPrecision(12, 3));
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Stamp the tenant on new tenant-owned rows that didn't set it explicitly.
        if (TenantId is int rid)
            foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
                if (entry.State == EntityState.Added && entry.Entity.RestaurantId == 0)
                    entry.Entity.RestaurantId = rid;

        return base.SaveChangesAsync(ct);
    }
}
