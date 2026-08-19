using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using QrDine.Api;

var builder = WebApplication.CreateBuilder(args);

// EF Core in-memory DB => the demo runs with ZERO setup (no SQL install).
// To go production: replace with UseNpgsql(connectionString) and add migrations.
builder.Services.AddDbContext<AppDb>(o => o.UseInMemoryDatabase("qrdine"));
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseCors();

// ---------- Seed a demo restaurant, tables and a Pakistani menu ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Seed.Run(db);
}

// Helper to load a restaurant by slug
async Task<Restaurant?> RestBySlug(AppDb db, string slug) =>
    await db.Restaurants.FirstOrDefaultAsync(r => r.Slug == slug);

// Broadcast helper — pushes an order to staff + that table's customers
async Task Broadcast(IHubContext<OrderHub> hub, AppDb db, Order order, string slug)
{
    var dto = OrderDto.From(order);
    await hub.Clients.Group(OrderHub.StaffGroup(slug)).SendAsync("orderChanged", dto);
    await hub.Clients.Group(OrderHub.TableGroup(slug, order.TableCode)).SendAsync("orderChanged", dto);
}

// ================= MENU (guest) =================
app.MapGet("/api/r/{slug}", async (string slug, AppDb db) =>
{
    var r = await RestBySlug(db, slug);
    if (r is null) return Results.NotFound();
    var cats = await db.Categories.Where(c => c.RestaurantId == r.Id)
        .OrderBy(c => c.SortOrder).ToListAsync();
    var items = await db.MenuItems.Where(i => i.RestaurantId == r.Id).ToListAsync();
    return Results.Ok(new
    {
        restaurant = new { r.Slug, r.Name, r.Currency,
            features = new { ordering = r.FeatureOrdering, kitchen = r.FeatureKitchen, waiter = r.FeatureWaiter } },
        menu = cats.Select(c => new
        {
            c.Id, c.Name,
            items = items.Where(i => i.CategoryId == c.Id).Select(i => new
            {
                i.Id, i.Name, i.NameUrdu, i.Description, i.Price, i.Emoji, i.Available, i.PrepMinutes
            })
        })
    });
});

app.MapGet("/api/r/{slug}/tables", async (string slug, AppDb db) =>
{
    var r = await RestBySlug(db, slug);
    if (r is null) return Results.NotFound();
    var tables = await db.Tables.Where(t => t.RestaurantId == r.Id).ToListAsync();
    return Results.Ok(tables.Select(t => new { t.Code, t.Name }));
});

// ================= PLACE ORDER (guest) =================
app.MapPost("/api/r/{slug}/orders", async (string slug, PlaceOrderDto body, AppDb db, IHubContext<OrderHub> hub) =>
{
    var r = await RestBySlug(db, slug);
    if (r is null) return Results.NotFound();
    if (!r.FeatureOrdering) return Results.BadRequest("Ordering not enabled for this restaurant.");

    var table = await db.Tables.FirstOrDefaultAsync(t => t.RestaurantId == r.Id && t.Code == body.TableCode);
    if (table is null) return Results.BadRequest("Unknown table.");
    if (body.Items is null || body.Items.Count == 0) return Results.BadRequest("Empty order.");

    var menu = await db.MenuItems.Where(i => i.RestaurantId == r.Id).ToListAsync();
    var order = new Order
    {
        RestaurantId = r.Id,
        TableId = table.Id,
        TableCode = table.Code,
        TableName = table.Name,
        Status = OrderStatus.Placed,
        CustomerNote = body.Note
    };
    foreach (var line in body.Items)
    {
        var mi = menu.FirstOrDefault(m => m.Id == line.MenuItemId);
        if (mi is null || !mi.Available) continue;
        order.Items.Add(new OrderItem
        {
            MenuItemId = mi.Id, Name = mi.Name, Price = mi.Price,
            Qty = Math.Clamp(line.Qty, 1, 50), Note = line.Note
        });
    }
    if (order.Items.Count == 0) return Results.BadRequest("No valid items.");

    db.Orders.Add(order);
    await db.SaveChangesAsync();
    await Broadcast(hub, db, order, slug);
    return Results.Ok(OrderDto.From(order));
});

// Guest: list my table's orders (for live tracking after refresh)
app.MapGet("/api/r/{slug}/tables/{code}/orders", async (string slug, string code, AppDb db) =>
{
    var r = await RestBySlug(db, slug);
    if (r is null) return Results.NotFound();
    var orders = await db.Orders.Include(o => o.Items)
        .Where(o => o.RestaurantId == r.Id && o.TableCode == code && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
        .OrderBy(o => o.CreatedAt).ToListAsync();
    return Results.Ok(orders.Select(OrderDto.From));
});

// ================= STAFF: list orders =================
app.MapGet("/api/r/{slug}/staff/orders", async (string slug, AppDb db) =>
{
    var r = await RestBySlug(db, slug);
    if (r is null) return Results.NotFound();
    var orders = await db.Orders.Include(o => o.Items)
        .Where(o => o.RestaurantId == r.Id && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
        .OrderBy(o => o.CreatedAt).ToListAsync();
    return Results.Ok(orders.Select(OrderDto.From));
});

// ================= STAFF: transitions =================
// Cashier approves a placed order
app.MapPost("/api/orders/{id}/approve", async (int id, AppDb db, IHubContext<OrderHub> hub) =>
    await Transition(id, db, hub, o => { if (o.Status == OrderStatus.Placed) o.Status = OrderStatus.Approved; }));

// Kitchen accepts + sets ETA (starts cooking)
app.MapPost("/api/orders/{id}/cook", async (int id, CookDto body, AppDb db, IHubContext<OrderHub> hub) =>
    await Transition(id, db, hub, o =>
    {
        if (o.Status is OrderStatus.Approved or OrderStatus.Placed)
        {
            o.Status = OrderStatus.Cooking;
            o.AcceptedAt = DateTime.UtcNow;
            o.EtaMinutes = Math.Clamp(body.EtaMinutes, 1, 180);
        }
    }));

// Kitchen marks ready
app.MapPost("/api/orders/{id}/ready", async (int id, AppDb db, IHubContext<OrderHub> hub) =>
    await Transition(id, db, hub, o => { if (o.Status == OrderStatus.Cooking) o.Status = OrderStatus.Ready; }));

// Waiter picks up the table / assigns self
app.MapPost("/api/orders/{id}/assign", async (int id, AssignDto body, AppDb db, IHubContext<OrderHub> hub) =>
    await Transition(id, db, hub, o => o.WaiterName = body.WaiterName));

// Waiter marks served
app.MapPost("/api/orders/{id}/serve", async (int id, AppDb db, IHubContext<OrderHub> hub) =>
    await Transition(id, db, hub, o => { if (o.Status == OrderStatus.Ready) o.Status = OrderStatus.Served; }));

// Cashier/waiter marks paid (closes it)
app.MapPost("/api/orders/{id}/pay", async (int id, AppDb db, IHubContext<OrderHub> hub) =>
    await Transition(id, db, hub, o => o.Status = OrderStatus.Paid));

// Cancel (with implicit reason in demo)
app.MapPost("/api/orders/{id}/cancel", async (int id, AppDb db, IHubContext<OrderHub> hub) =>
    await Transition(id, db, hub, o => o.Status = OrderStatus.Cancelled));

// Shared transition runner
async Task<IResult> Transition(int id, AppDb db, IHubContext<OrderHub> hub, Action<Order> mutate)
{
    var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
    if (o is null) return Results.NotFound();
    mutate(o);
    await db.SaveChangesAsync();
    var slug = (await db.Restaurants.FirstAsync(r => r.Id == o.RestaurantId)).Slug;
    await Broadcast(hub, db, o, slug);
    return Results.Ok(OrderDto.From(o));
}

app.MapHub<OrderHub>("/hub/orders");
app.MapGet("/", () => "QrDine API running. See /api/r/desidhaba");

app.Run();

// ---------- DTOs ----------
record PlaceOrderDto(string TableCode, List<OrderLineDto> Items, string? Note);
record OrderLineDto(int MenuItemId, int Qty, string? Note);
record CookDto(int EtaMinutes);
record AssignDto(string WaiterName);

record OrderDto(int Id, string TableCode, string TableName, string Status, DateTime CreatedAt,
    DateTime? AcceptedAt, int EtaMinutes, string? WaiterName, string? CustomerNote,
    decimal Total, List<OrderItemDto> Items)
{
    public static OrderDto From(Order o) => new(o.Id, o.TableCode, o.TableName, o.Status.ToString(),
        o.CreatedAt, o.AcceptedAt, o.EtaMinutes, o.WaiterName, o.CustomerNote, o.Total,
        o.Items.Select(i => new OrderItemDto(i.Name, i.Price, i.Qty, i.Note)).ToList());
}
record OrderItemDto(string Name, decimal Price, int Qty, string? Note);
