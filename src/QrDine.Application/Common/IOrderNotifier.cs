using QrDine.Application.Ordering.Dtos;

namespace QrDine.Application.Common;

/// <summary>
/// Pushes an order change out to the live screens. Infrastructure implements
/// this with SignalR; the Application layer just says "this order changed" and
/// stays unaware of the transport.
/// </summary>
public interface IOrderNotifier
{
    Task OrderChanged(string slug, OrderDto order, CancellationToken ct = default);

    /// <summary>The order moved to another table: phones still on the old table should drop it.</summary>
    Task OrderMoved(string slug, string fromTableCode, OrderDto order, CancellationToken ct = default);

    /// <summary>A guest called for a waiter or the bill, or staff dealt with it.</summary>
    Task ServiceRequestChanged(string slug, Ordering.ServiceRequestDto request, CancellationToken ct = default);
}
