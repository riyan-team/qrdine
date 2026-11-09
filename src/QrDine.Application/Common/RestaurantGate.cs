using System.Collections.Concurrent;

namespace QrDine.Application.Common;

/// <summary>
/// One change at a time for each restaurant.
///
/// Settling a bill, approving an order and shifting a table all read the current state, decide,
/// then write. When two of those overlap (two cashiers, a double tap, ten phones at one table) both
/// decide from the same old state: that is how a table gets two bills, stock comes off twice, or an
/// order lands on a seating nobody bills. Taking turns removes that whole class of problem without
/// changing any of the rules themselves.
///
/// A restaurant makes a handful of changes a second at most and each takes milliseconds, so nobody
/// notices the queue, and one restaurant never waits for another.
///
/// This works because the app runs as one process. A second copy of the app would need a lock the
/// copies share (a Postgres advisory lock). The unique indexes on seatings and bills hold either way.
/// </summary>
public class RestaurantGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    public Task<bool> Enter(int restaurantId, TimeSpan wait, CancellationToken ct = default) =>
        _gates.GetOrAdd(restaurantId, _ => new SemaphoreSlim(1, 1)).WaitAsync(wait, ct);

    public void Leave(int restaurantId) => _gates[restaurantId].Release();
}

/// <summary>This request's turn at the gate. Scoped, so each request has its own.</summary>
public class FloorTurn
{
    private readonly RestaurantGate _gate;
    private int? _holding;

    public FloorTurn(RestaurantGate gate) => _gate = gate;

    public async Task<bool> Take(int restaurantId, TimeSpan wait, CancellationToken ct = default)
    {
        if (!await _gate.Enter(restaurantId, wait, ct)) return false;
        _holding = restaurantId;
        return true;
    }

    /// <summary>Done with the shared state. Safe to call twice, so a slow step that only touches its
    /// own row (filing a bill with FBR) can finish early and the floor doesn't wait on it.</summary>
    public void Finish()
    {
        if (_holding is int id)
        {
            _holding = null;
            _gate.Leave(id);
        }
    }
}
