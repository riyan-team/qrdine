using Microsoft.EntityFrameworkCore;

namespace QrDine.Api;

// ---------- Entities ----------
// NOTE: Every tenant-owned row carries RestaurantId. That is the seed of
// multi-tenancy. When you move to PostgreSQL you add a global query filter
// on RestaurantId and this same model serves many restaurants from one DB.

public class Restaurant
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";      // used in the QR URL: /r/{slug}/t/{code}
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "Rs";
    // Feature flags — this is how you sell modules. Flip per restaurant.
    public bool FeatureOrdering { get; set; } = true;
    public bool FeatureKitchen { get; set; } = true;
    public bool FeatureWaiter { get; set; } = true;
}

public class DiningTable
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Code { get; set; } = "";      // e.g. "T1" — part of the QR URL
    public string Name { get; set; } = "";      // e.g. "Table 1 (Window)"
}

public class MenuCategory
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class MenuItem
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string NameUrdu { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Emoji { get; set; } = "🍽️";  // stand-in for a photo in the demo
    public bool Available { get; set; } = true;
    public int PrepMinutes { get; set; } = 15;   // default ETA; kitchen can override
}

// Order lifecycle — this is the whole point of the product:
// Placed -> Approved (cashier) -> Cooking (kitchen sets ETA) -> Ready -> Served -> Paid
public enum OrderStatus { Placed, Approved, Cooking, Ready, Served, Paid, Cancelled }

public class Order
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int TableId { get; set; }
    public string TableCode { get; set; } = "";
    public string TableName { get; set; } = "";
    public OrderStatus Status { get; set; } = OrderStatus.Placed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }    // when kitchen started cooking
    public int EtaMinutes { get; set; }          // kitchen's promised time
    public string? WaiterName { get; set; }
    public string? CustomerNote { get; set; }
    public List<OrderItem> Items { get; set; } = new();

    public decimal Total => Items.Sum(i => i.Price * i.Qty);
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Qty { get; set; } = 1;
    public string? Note { get; set; }            // e.g. "no onion", "extra spicy"
}

// ---------- DbContext ----------
public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<DiningTable> Tables => Set<DiningTable>();
    public DbSet<MenuCategory> Categories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
}
