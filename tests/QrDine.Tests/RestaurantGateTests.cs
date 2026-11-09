using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Tables;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// Two cashiers, a double tap, ten phones at one table: changes must take turns, and the database
/// must refuse the two states that would quietly lose money or food.
/// </summary>
public class RestaurantGateTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task One_restaurants_changes_never_overlap()
    {
        var gate = new RestaurantGate();
        int inside = 0, most = 0;

        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
        {
            var turn = new FloorTurn(gate);
            Assert.True(await turn.Take(1, Wait));
            try
            {
                most = Math.Max(most, Interlocked.Increment(ref inside));
                await Task.Delay(2);
                Interlocked.Decrement(ref inside);
            }
            finally { turn.Finish(); }
        })));

        Assert.Equal(1, most);
    }

    [Fact]
    public async Task One_restaurant_never_waits_for_another()
    {
        var gate = new RestaurantGate();
        var karachi = new FloorTurn(gate);
        Assert.True(await karachi.Take(1, Wait));

        var lahore = new FloorTurn(gate);
        Assert.True(await lahore.Take(2, TimeSpan.FromMilliseconds(50)));

        karachi.Finish();
        lahore.Finish();
    }

    [Fact]
    public async Task A_stuck_turn_times_out_instead_of_hanging_forever()
    {
        var gate = new RestaurantGate();
        var first = new FloorTurn(gate);
        Assert.True(await first.Take(1, Wait));

        var second = new FloorTurn(gate);
        Assert.False(await second.Take(1, TimeSpan.FromMilliseconds(20)));

        // Finishing a turn it never got must not open the gate for anyone else.
        second.Finish();
        var third = new FloorTurn(gate);
        Assert.False(await third.Take(1, TimeSpan.FromMilliseconds(20)));

        first.Finish();
        Assert.True(await third.Take(1, Wait));
        third.Finish();
    }

    [Fact]
    public async Task Finishing_early_and_again_at_the_end_lets_only_one_person_through()
    {
        // Settling finishes early so FBR can't hold up the floor, then the filter finishes as usual.
        var gate = new RestaurantGate();
        var settling = new FloorTurn(gate);
        Assert.True(await settling.Take(1, Wait));
        settling.Finish();

        var next = new FloorTurn(gate);
        Assert.True(await next.Take(1, Wait));
        settling.Finish();

        var after = new FloorTurn(gate);
        Assert.False(await after.Take(1, TimeSpan.FromMilliseconds(20)));
        next.Finish();
    }
}

public class OneSeatingOneBillTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly CurrentTenant _tenant = new();

    public OneSeatingOneBillTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant);
        _db.Database.EnsureCreated();
        _db.Restaurants.Add(new Restaurant { Slug = "test", Name = "Test" });
        _db.SaveChanges();
        _tenant.Set(1, "test");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private TableSession Seating(int table, bool open = true) => new()
    {
        RestaurantId = 1, BranchId = 1, DiningTableId = table, TableCode = $"T{table}", TableName = $"Table {table}", IsOpen = open,
    };

    private static Invoice Bill(int seating, int number) => new()
    {
        RestaurantId = 1, BranchId = 1, TableSessionId = seating, Number = number, Usin = $"USIN{number:D6}",
        TableCode = "T1", TableName = "Table 1", PaymentMethod = "Cash",
    };

    [Fact]
    public async Task A_table_cant_have_two_running_seatings()
    {
        _db.Sessions.Add(Seating(1));
        await _db.SaveChangesAsync();

        _db.Sessions.Add(Seating(1));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Earlier_guests_finished_seatings_dont_block_the_next_guests()
    {
        _db.Sessions.AddRange(Seating(1, open: false), Seating(1, open: false), Seating(1), Seating(2));
        await _db.SaveChangesAsync();
        Assert.Equal(4, await _db.Sessions.CountAsync());
    }

    [Fact]
    public async Task A_seating_cant_be_billed_twice()
    {
        _db.Invoices.Add(Bill(seating: 7, number: 1));
        await _db.SaveChangesAsync();

        _db.Invoices.Add(Bill(seating: 7, number: 2));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }
}
