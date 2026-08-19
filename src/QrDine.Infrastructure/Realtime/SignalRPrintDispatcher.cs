using Microsoft.AspNetCore.SignalR;
using QrDine.Application.Common;
using QrDine.Application.Printing;

namespace QrDine.Infrastructure.Realtime;

/// <summary>Pushes a print job to the restaurant's on-site print bridge(s).</summary>
public class SignalRPrintDispatcher : IPrintDispatcher
{
    private readonly IHubContext<OrderHub> _hub;
    public SignalRPrintDispatcher(IHubContext<OrderHub> hub) => _hub = hub;

    public Task Dispatch(string slug, PrintJob job, CancellationToken ct = default)
        => _hub.Clients.Group(OrderHub.PrintGroup(slug)).SendAsync("print", job, ct);
}
