using Microsoft.AspNetCore.SignalR;
using QrDine.Application.Common;
using QrDine.Application.Ordering.Dtos;

namespace QrDine.Infrastructure.Realtime;

/// <summary>
/// The SignalR implementation of the Application's IOrderNotifier. Pushes each
/// order change to the restaurant's staff group and to the order's table group.
/// </summary>
public class SignalROrderNotifier : IOrderNotifier
{
    private readonly IHubContext<OrderHub> _hub;
    public SignalROrderNotifier(IHubContext<OrderHub> hub) => _hub = hub;

    public async Task OrderChanged(string slug, OrderDto order, CancellationToken ct = default)
    {
        await _hub.Clients.Group(OrderHub.StaffGroup(slug)).SendAsync("orderChanged", order, ct);
        await _hub.Clients.Group(OrderHub.TableGroup(slug, order.TableCode)).SendAsync("orderChanged", order, ct);
    }

    public Task OrderMoved(string slug, string fromTableCode, OrderDto order, CancellationToken ct = default) =>
        _hub.Clients.Group(OrderHub.TableGroup(slug, fromTableCode)).SendAsync("orderChanged", order, ct);

    public async Task ServiceRequestChanged(string slug, Application.Ordering.ServiceRequestDto request, CancellationToken ct = default)
    {
        await _hub.Clients.Group(OrderHub.StaffGroup(slug)).SendAsync("serviceRequest", request, ct);
        await _hub.Clients.Group(OrderHub.TableGroup(slug, request.TableCode)).SendAsync("serviceRequest", request, ct);
    }
}
