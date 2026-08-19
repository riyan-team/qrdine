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
}
