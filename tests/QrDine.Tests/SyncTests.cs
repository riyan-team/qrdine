using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Application.Sync;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Menu;
using QrDine.Domain.Ordering;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Sync;
using QrDine.Domain.Tables;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using QrDine.Infrastructure.Sync;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// Phase 1 of running on a restaurant's own PC: every synced row gets a SyncId and LastChangedAt,
/// each save writes a change-list line (when sync is on), a paired device reads only its own
/// restaurant, and sending the same changes twice is a no-op.
/// </summary>
public class SyncTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly CurrentTenant _tenant = new();
    private static readonly SyncSettings On = new(true, SettleSeconds: 0);
    private readonly AppDbContext _db;

    public SyncTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = Context(On);
        _db.Database.EnsureCreated();
        _db.Restaurants.AddRange(new Restaurant { Slug = "cafe", Name = "Cafe" }, new Restaurant { Slug = "other", Name = "Other" });
        _db.SaveChanges();
        _tenant.Set(1, "cafe");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private AppDbContext Context(SyncSettings sync) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant, sync);

    private SyncService Service(SyncSettings? s = null) => new(_db, new EfSyncStore(_db), s ?? On);

    private Guid SyncIdOf(object row) => (Guid)_db.Entry(row).Property(SyncModel.SyncIdProperty).CurrentValue!;
    private DateTime? ChangedAtOf(object row) => (DateTime?)_db.Entry(row).Property(SyncModel.LastChangedAtProperty).CurrentValue;
    private List<SyncChange> Lines(int restaurantId = 1) =>
        _db.SyncChanges.IgnoreQueryFilters().Where(c => c.RestaurantId == restaurantId).OrderBy(c => c.Id).ToList();

    private async Task<MenuItem> Dish(string name, decimal price = 500, int restaurantId = 1)
    {
        var category = new MenuCategory { RestaurantId = restaurantId, Name = "Mains" };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        var item = new MenuItem
        {
            RestaurantId = restaurantId, CategoryId = category.Id, Name = name, Price = price,
            Variants = { new MenuItemVariant { Name = "Half", Price = price / 2 }, new MenuItemVariant { Name = "Full", Price = price } },
        };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    private Order NewOrder(string table = "T1")
    {
        var order = Order.Place(1, new TableSessionInfo(0, 0, table, "Table " + table, 0, "Main"), null);
        order.AddItem(1, "Biryani", "Full", 500, 2, null, new[] { ("Extra raita", 50m) });
        order.AddItem(1, "Naan", null, 50, 3, null);
        return order;
    }

    private async Task<(SyncDevice Device, string Key)> PairedDevice(int restaurantId = 1)
    {
        _tenant.Set(restaurantId, restaurantId == 1 ? "cafe" : "other");
        var paired = await Service().Pair("Counter PC", "owner");
        var device = (await Service().Authenticate(paired.Key))!;
        return (device, paired.Key);
    }

    // ---------- which tables sync ----------

    [Fact]
    public void Every_restaurant_table_syncs_and_platform_tables_do_not()
    {
        var types = _db.Model.GetEntityTypes().ToList();
        var roots = types.Where(SyncModel.IsRoot).Select(t => t.ClrType.Name).OrderBy(x => x).ToList();
        var children = types.Where(t => SyncModel.IsSynced(t) && !SyncModel.IsRoot(t)).Select(t => t.ClrType.Name).OrderBy(x => x).ToList();
        var never = types.Where(t => !SyncModel.IsSynced(t)).Select(t => t.ClrType.Name).OrderBy(x => x).ToList();

        Assert.Equal(new[] { "InvoiceLine", "MenuItemVariant", "ModifierGroup", "ModifierOption", "OrderItem", "OrderItemModifier", "RecipeLine", "SupplierEntryLine" }, children);
        Assert.Equal(new[] { "Plan", "PlatformAdmin", "StoredFile", "SubscriptionPayment", "SyncChange", "SyncDevice", "SyncInboxEntry" }, never);
        // A new restaurant table has to choose how it syncs before this passes.
        Assert.Equal(roots, SyncRules.Types.OrderBy(x => x).ToList());
        Assert.Contains("Restaurant", roots);
    }

    // ---------- every row gets a SyncId and LastChangedAt ----------

    [Fact]
    public async Task New_rows_get_their_own_sync_id_and_change_time()
    {
        var item = await Dish("Biryani");
        var ids = new[] { SyncIdOf(item), SyncIdOf(item.Variants[0]), SyncIdOf(item.Variants[1]) };
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(3, ids.Distinct().Count());
        Assert.NotNull(ChangedAtOf(item));
        Assert.True(item.Id > 0);   // the int ids are unchanged
    }

    [Fact]
    public async Task With_sync_off_rows_still_get_sync_ids_but_no_change_lines_are_written()
    {
        using var off = Context(SyncSettings.Off);
        var table = new DiningTable { Code = "T9", Name = "Table 9" };
        off.Tables.Add(table);
        await off.SaveChangesAsync();
        Assert.NotEqual(Guid.Empty, (Guid)off.Entry(table).Property(SyncModel.SyncIdProperty).CurrentValue!);
        Assert.Empty(Lines());
    }

    // ---------- the change list ----------

    [Fact]
    public async Task An_order_with_items_and_modifiers_makes_one_line_for_the_order()
    {
        var order = NewOrder();
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var line = Assert.Single(Lines());
        Assert.Equal(("Order", SyncOp.Upsert, SyncIdOf(order)), (line.EntityType, line.Op, line.SyncId));
        Assert.All(order.Items, i => Assert.NotEqual(Guid.Empty, SyncIdOf(i)));
        Assert.NotEqual(Guid.Empty, SyncIdOf(order.Items.First().Modifiers.Single()));
    }

    [Fact]
    public async Task Changing_a_child_row_marks_its_root_as_changed()
    {
        var item = await Dish("Biryani");
        await _db.Database.ExecuteSqlRawAsync("UPDATE MenuItems SET LastChangedAt = '2020-01-01 00:00:00'");
        await _db.Entry(item).ReloadAsync();
        var before = Lines().Count;

        item.Variants[0].Price = 275;
        await _db.SaveChangesAsync();

        var line = Lines().Last();
        Assert.Equal(before + 1, Lines().Count);
        Assert.Equal(("MenuItem", SyncIdOf(item)), (line.EntityType, line.SyncId));
        Assert.True(ChangedAtOf(item) > new DateTime(2021, 1, 1));
    }

    [Fact]
    public async Task Deleting_a_row_writes_a_delete_line()
    {
        var table = new DiningTable { Code = "T1", Name = "Table 1" };
        _db.Tables.Add(table);
        await _db.SaveChangesAsync();
        var id = SyncIdOf(table);

        _db.Tables.Remove(table);
        await _db.SaveChangesAsync();

        var line = Lines().Last();
        Assert.Equal(("DiningTable", SyncOp.Delete, id), (line.EntityType, line.Op, line.SyncId));
    }

    [Fact]
    public async Task A_failed_save_leaves_no_change_lines_behind_and_its_retry_writes_one()
    {
        // The same shape as ticket numbering: a clash on the unique code, then a retry on the same row.
        _db.Tables.Add(new DiningTable { Code = "T1", Name = "Table 1" });
        await _db.SaveChangesAsync();
        var clash = new DiningTable { Code = "T1", Name = "Table 1 again" };
        _db.Tables.Add(clash);
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());

        clash.Code = "T2";
        await _db.SaveChangesAsync();

        var lines = Lines();
        Assert.Equal(2, lines.Count);
        Assert.Single(lines, l => l.SyncId == SyncIdOf(clash));
    }

    // ---------- pairing a device ----------

    [Fact]
    public async Task Pairing_gives_the_key_once_and_keeps_only_its_hash()
    {
        var paired = await Service().Pair("  Counter PC ", "03001234567");
        Assert.StartsWith("dk_", paired.Key);
        Assert.Equal("Counter PC", paired.Name);

        var row = _db.SyncDevices.Single();
        Assert.NotEqual(paired.Key, row.KeyHash);
        Assert.DoesNotContain(paired.Key, row.KeyHash);
        Assert.Equal(1, row.RestaurantId);

        var device = await Service().Authenticate(paired.Key);
        Assert.Equal(row.Id, device!.Id);
        Assert.Null(await Service().Authenticate(paired.Key + "x"));
        Assert.Null(await Service().Authenticate(""));
        Assert.Null(await Service().Authenticate(null));
    }

    [Fact]
    public async Task A_removed_device_is_refused_at_once()
    {
        var (device, key) = await PairedDevice();
        await Service().Revoke(device.Id);
        Assert.Null(await Service().Authenticate(key));
    }

    [Fact]
    public async Task A_restaurant_pairs_one_counter_pc_and_it_needs_a_name()
    {
        Assert.Equal(1, SyncService.MaxActiveDevices);   // two PCs would both give out bill numbers
        for (var i = 0; i < SyncService.MaxActiveDevices; i++) await Service().Pair($"PC {i}", "owner");
        await Assert.ThrowsAsync<DomainException>(() => Service().Pair("PC 2", "owner"));
        await Assert.ThrowsAsync<DomainException>(() => Service().Pair("  ", "owner"));
    }

    // ---------- a device sees only its own restaurant ----------

    [Fact]
    public async Task A_device_only_ever_reads_its_own_restaurant()
    {
        _tenant.Set(2, "other");
        await Dish("Other's secret dish", restaurantId: 2);
        _tenant.Set(1, "cafe");
        await Dish("Cafe biryani");
        var (device, _) = await PairedDevice(1);

        var pull = await Service().Pull(device, 0, 500);
        var snap = await Service().Snapshot(device, "MenuItem", 0, 500);
        var everything = JsonSerializer.Serialize(new { pull, snap });
        Assert.Contains("Cafe biryani", everything);
        Assert.DoesNotContain("Other's secret dish", everything);
        // Rows travel without this side's ids (each side has its own): every row pulled is one of the cafe's.
        Assert.All(pull.Changes, c => Assert.False(c.Data?.ContainsKey("RestaurantId") == true || c.Data?.ContainsKey("Id") == true));
        var cafeRows = _db.MenuItems.IgnoreQueryFilters().Where(i => i.RestaurantId == 1).Select(i => EF.Property<Guid>(i, "SyncId")).ToList();
        Assert.All(pull.Changes.Where(c => c.Type == "MenuItem"), c => Assert.Contains(c.SyncId, cafeRows));

        // Even with the wrong restaurant in the tenant filter, the device's own restaurant check still holds.
        _tenant.Set(2, "other");
        var crossed = JsonSerializer.Serialize(new { pull = await Service().Pull(device, 0, 500), snap = await Service().Snapshot(device, "MenuItem", 0, 500) });
        Assert.DoesNotContain("Other's secret dish", crossed);
        Assert.DoesNotContain("Cafe biryani", crossed);
    }

    [Fact]
    public async Task A_device_pushes_into_its_own_restaurant_only()
    {
        var (device, _) = await PairedDevice(1);
        var data = JsonSerializer.SerializeToElement(new { RestaurantId = 2, Name = "Sneaky" });
        await Service().Push(device, new[] { new SyncPushChange(Guid.NewGuid(), "MenuItem", Guid.NewGuid(), SyncOp.Upsert, DateTime.UtcNow, data) });

        var entry = Assert.Single(_db.SyncInbox.IgnoreQueryFilters().ToList());
        Assert.Equal((1, device.Id), (entry.RestaurantId, entry.DeviceId));
    }

    // ---------- push is idempotent ----------

    [Fact]
    public async Task Sending_the_same_changes_again_is_a_no_op()
    {
        var (device, _) = await PairedDevice();
        var row = JsonSerializer.SerializeToElement(new { Name = "Table 5" });
        var batch = Enumerable.Range(0, 3)
            .Select(_ => new SyncPushChange(Guid.NewGuid(), "DiningTable", Guid.NewGuid(), SyncOp.Upsert, DateTime.UtcNow, row))
            .Append(new SyncPushChange(Guid.NewGuid(), "DiningTable", Guid.NewGuid(), SyncOp.Delete, DateTime.UtcNow, null))
            .ToList();
        batch.Add(batch[0]);   // the same change twice in one batch counts once

        var first = await Service().Push(device, batch);
        var again = await Service().Push(device, batch);

        Assert.Equal((4, 1), (first.Accepted, first.AlreadyHad));
        Assert.Equal((0, 5), (again.Accepted, again.AlreadyHad));
        Assert.Equal(4, _db.SyncInbox.Count());
        Assert.All(_db.SyncInbox.ToList(), e => Assert.Equal(SyncInboxStatus.Pending, e.Status));
    }

    [Fact]
    public async Task Push_refuses_changes_it_cannot_store_safely()
    {
        var (device, _) = await PairedDevice();
        var row = JsonSerializer.SerializeToElement(new { Name = "x" });
        SyncPushChange Change(string type = "MenuItem", string op = SyncOp.Upsert, JsonElement? data = null, Guid? changeId = null) =>
            new(changeId ?? Guid.NewGuid(), type, Guid.NewGuid(), op, DateTime.UtcNow, data ?? row);

        await Assert.ThrowsAsync<DomainException>(() => Service().Push(device, new[] { Change(type: "PlatformAdmin") }));
        await Assert.ThrowsAsync<DomainException>(() => Service().Push(device, new[] { Change(type: "SyncDevice") }));
        await Assert.ThrowsAsync<DomainException>(() => Service().Push(device, new[] { Change(op: "Merge") }));
        await Assert.ThrowsAsync<DomainException>(() => Service().Push(device, new[] { Change(data: JsonSerializer.SerializeToElement(new[] { 1 })) }));
        await Assert.ThrowsAsync<DomainException>(() => Service().Push(device, new[] { Change(changeId: Guid.Empty) }));
        await Assert.ThrowsAsync<DomainException>(() => Service().Push(device, Array.Empty<SyncPushChange>()));
        Assert.Empty(_db.SyncInbox.ToList());
    }

    // ---------- pull since N ----------

    [Fact]
    public async Task Pull_gives_each_changed_row_once_as_it_is_now_with_its_child_rows()
    {
        var item = await Dish("Biryani", 500);
        item.Price = 520;
        await _db.SaveChangesAsync();
        item.Price = 540;
        await _db.SaveChangesAsync();
        var order = NewOrder();
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        var (device, _) = await PairedDevice();

        var page = await Service().Pull(device, 0, 500);

        var dish = Assert.Single(page.Changes, c => c.Type == "MenuItem");
        Assert.Equal(540m, dish.Data!["Price"]);
        Assert.Equal(2, ((List<Dictionary<string, object?>>)dish.Data["Variants"]!).Count);
        var placed = Assert.Single(page.Changes, c => c.Type == "Order");
        var items = (List<Dictionary<string, object?>>)placed.Data!["Items"]!;
        Assert.Equal(2, items.Count);
        Assert.Single(items, i => ((List<Dictionary<string, object?>>)i["Modifiers"]!).Count == 1);
        Assert.Equal("Placed", placed.Data["Status"]);
        Assert.Equal(SyncIdOf(order), placed.SyncId);
        Assert.False(page.More);
    }

    [Fact]
    public async Task Pull_since_a_line_gives_only_later_changes_one_page_at_a_time()
    {
        foreach (var code in new[] { "T1", "T2", "T3" })
        {
            _db.Tables.Add(new DiningTable { Code = code, Name = code });
            await _db.SaveChangesAsync();
        }
        var lines = Lines();
        var (device, _) = await PairedDevice();

        var later = await Service().Pull(device, lines[0].Id, 500);
        Assert.Equal(new[] { "T2", "T3" }, later.Changes.Select(c => (string)c.Data!["Code"]!));

        var first = await Service().Pull(device, lines[0].Id, 1);
        Assert.True(first.More);
        Assert.Equal(lines[1].Id, first.Next);
        var second = await Service().Pull(device, first.Next, 1);
        Assert.Equal("T3", second.Changes.Single().Data!["Code"]);
    }

    [Fact]
    public async Task A_deleted_row_pulls_as_a_delete()
    {
        var table = new DiningTable { Code = "T1", Name = "Table 1" };
        _db.Tables.Add(table);
        await _db.SaveChangesAsync();
        var id = SyncIdOf(table);
        _db.Tables.Remove(table);
        await _db.SaveChangesAsync();
        var (device, _) = await PairedDevice();

        var change = Assert.Single((await Service().Pull(device, 0, 500)).Changes, c => c.SyncId == id);
        Assert.Equal(SyncOp.Delete, change.Op);
        Assert.Null(change.Data);
    }

    [Fact]
    public async Task Pull_leaves_out_changes_younger_than_the_settle_time()
    {
        _db.Tables.Add(new DiningTable { Code = "T1", Name = "Table 1" });
        await _db.SaveChangesAsync();
        var (device, _) = await PairedDevice();

        var page = await Service(new SyncSettings(true, SettleSeconds: 60)).Pull(device, 0, 500);
        Assert.Empty(page.Changes);
        Assert.Equal(0, page.Next);
    }

    // ---------- first download ----------

    [Fact]
    public async Task Hello_and_snapshot_give_a_new_device_everything_and_where_to_pull_from()
    {
        foreach (var code in new[] { "T1", "T2", "T3" }) _db.Tables.Add(new DiningTable { Code = code, Name = code });
        await _db.SaveChangesAsync();
        _tenant.Set(2, "other");
        _db.Tables.Add(new DiningTable { Code = "X1", Name = "Other's" });
        await _db.SaveChangesAsync();
        var (device, _) = await PairedDevice(1);
        var cafe = _db.Restaurants.Single(r => r.Id == 1);

        var hello = await Service().Hello(device, cafe);
        Assert.Equal(Lines(1).Max(l => l.Id), hello.Head);
        Assert.Contains("Order", hello.Types);
        Assert.DoesNotContain("SyncDevice", hello.Types);

        var page1 = await Service().Snapshot(device, "DiningTable", 0, 2);
        var page2 = await Service().Snapshot(device, "DiningTable", page1.Next!.Value, 2);
        Assert.Equal(new[] { "T1", "T2", "T3" }, page1.Items.Concat(page2.Items).Select(r => (string)r.Data["Code"]!));
        Assert.Null(page2.Next);
        await Assert.ThrowsAsync<DomainException>(() => Service().Snapshot(device, "PlatformAdmin", 0, 10));
    }

    [Fact]
    public async Task Fbr_tokens_never_leave_the_server_and_sign_in_hashes_go_only_to_the_paired_pc()
    {
        _db.Staff.Add(new StaffMember
        {
            Username = "cashier", DisplayName = "Cashier", Role = StaffRole.Cashier,
            PasswordHash = "the-password-hash", FbrToken = "the-fbr-token",
        });
        await _db.SaveChangesAsync();
        var (device, _) = await PairedDevice();

        var snap = await Service().Snapshot(device, "StaffMember", 0, 50);
        var pull = await Service().Pull(device, 0, 500);
        var json = JsonSerializer.Serialize(new { snap, pull });

        Assert.Contains("cashier", json);
        Assert.DoesNotContain("the-fbr-token", json);
        Assert.DoesNotContain("FbrToken", json);
        Assert.DoesNotContain(_db.SyncDevices.Single().KeyHash, json);
        // The PC signs staff in with no internet, so their (salted PBKDF2) hash goes to it, and only over the device key.
        Assert.Contains("the-password-hash", json);
    }

    [Fact]
    public void Every_column_that_points_at_another_row_travels_as_that_rows_sync_id()
    {
        var roots = _db.Model.GetEntityTypes().Where(SyncModel.IsRoot).Select(SyncModel.NameOf).ToHashSet();
        foreach (var t in _db.Model.GetEntityTypes().Where(SyncModel.IsSynced))
        {
            var name = SyncModel.NameOf(t);
            var parent = SyncModel.IsRoot(t) ? null : SyncModel.ParentKey(t).Properties[0].Name;
            foreach (var p in t.GetProperties().Where(p => (p.ClrType == typeof(int) || p.ClrType == typeof(int?)) && p.Name.EndsWith("Id") && !p.IsPrimaryKey()))
            {
                if (p.Name == "RestaurantId" || p.Name == parent || SyncRules.IsServerOnly(name, p.Name)) continue;
                Assert.True(SyncRules.ReferenceOf(name, p.Name) is not null, $"{name}.{p.Name} points at another row: add it to SyncRules.References.");
            }
        }
        foreach (var target in SyncRules.References.Values.SelectMany(r => r.Values)) Assert.Contains(target, roots);
        // A new copy is filled in an order where every row's references already exist.
        var order = new EfSyncStore(_db).InDependencyOrder.ToList();
        Assert.Equal("Restaurant", order[0]);
        Assert.True(order.IndexOf("MenuCategory") < order.IndexOf("MenuItem"));
        Assert.True(order.IndexOf("StockItem") < order.IndexOf("MenuItem"));     // recipe lines
        Assert.True(order.IndexOf("MenuItem") < order.IndexOf("Order"));         // order items
        Assert.True(order.IndexOf("TableSession") < order.IndexOf("Invoice"));
        Assert.True(order.IndexOf("Invoice") < order.IndexOf("CustomerEntry"));
    }

    // ---------- the conflict rules ----------

    private static readonly DateTime T0 = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_row_the_other_side_does_not_have_is_always_applied()
    {
        foreach (var rule in Enum.GetValues<SyncRule>())
            Assert.Equal(SyncDecision.Apply, SyncRules.Decide(rule, false, SyncSide.Server, null, SyncSide.Device, T0));
    }

    [Fact]
    public void For_settings_the_newest_change_wins_and_a_tie_keeps_what_is_there()
    {
        var rule = SyncRules.For("MenuItem");
        Assert.Equal(SyncRule.NewestWins, rule);
        Assert.Equal(SyncDecision.Apply, SyncRules.Decide(rule, true, SyncSide.Server, T0, SyncSide.Device, T0.AddSeconds(1)));
        Assert.Equal(SyncDecision.Skip, SyncRules.Decide(rule, true, SyncSide.Server, T0, SyncSide.Device, T0.AddSeconds(-1)));
        Assert.Equal(SyncDecision.Skip, SyncRules.Decide(rule, true, SyncSide.Server, T0, SyncSide.Device, T0));
        Assert.Equal(SyncDecision.Apply, SyncRules.Decide(rule, true, SyncSide.Server, null, SyncSide.Device, T0));
    }

    [Fact]
    public void A_bill_is_only_ever_changed_where_it_was_made()
    {
        var rule = SyncRules.For("Invoice");
        Assert.Equal(SyncRule.OwnedByOrigin, rule);
        // The PC can't overwrite a bill the server made, however new its copy is.
        Assert.Equal(SyncDecision.Skip, SyncRules.Decide(rule, true, SyncSide.Server, T0, SyncSide.Device, T0.AddHours(1)));
        // The PC's own later change to its own bill goes through.
        Assert.Equal(SyncDecision.Apply, SyncRules.Decide(rule, true, SyncSide.Device, T0, SyncSide.Device, T0.AddMinutes(1)));
        Assert.Equal(SyncDecision.Skip, SyncRules.Decide(rule, true, SyncSide.Device, T0, SyncSide.Device, T0.AddMinutes(-1)));
        Assert.Equal(SyncRule.OwnedByOrigin, SyncRules.For("Order"));
        Assert.Equal(SyncRule.OwnedByOrigin, SyncRules.For("Payment"));
    }

    [Fact]
    public void Stock_and_points_counts_come_from_the_pc_and_the_plan_never_travels()
    {
        Assert.Equal(SyncRule.Ledger, SyncRules.For("StockMovement"));
        Assert.Equal(SyncDecision.Skip, SyncRules.Decide(SyncRule.Ledger, true, SyncSide.Device, T0, SyncSide.Device, T0.AddHours(1)));
        Assert.True(SyncRules.IsDeviceOwned("StockItem", "Quantity"));
        Assert.True(SyncRules.IsDeviceOwned("LoyaltyMember", "Points"));
        Assert.True(SyncRules.IsDeviceOwned("DiningTable", "GuestPassesFrom"));
        Assert.True(SyncRules.IsServerOnly("Restaurant", "PlanExpiresAt"));
        Assert.True(SyncRules.IsServerOnly("Restaurant", "FeatureFbr"));
        Assert.True(SyncRules.IsFloor("Invoice") && SyncRules.IsFloor("StockMovement") && !SyncRules.IsFloor("MenuItem"));
        Assert.Throws<ArgumentException>(() => SyncRules.For("PlatformAdmin"));
    }

    [Fact]
    public void Bill_numbers_come_from_the_restaurants_pc_once_it_has_one()
    {
        Assert.Equal(SyncSide.Server, SyncRules.BillNumbersFrom(restaurantHasActivePc: false));
        Assert.Equal(SyncSide.Device, SyncRules.BillNumbersFrom(restaurantHasActivePc: true));
    }
}
