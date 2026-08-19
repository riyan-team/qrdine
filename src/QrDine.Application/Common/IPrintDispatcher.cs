using QrDine.Application.Printing;

namespace QrDine.Application.Common;

/// <summary>
/// Sends a print job to the restaurant's on-site print bridge. Infrastructure
/// implements this over SignalR (a "printers" group); the bridge picks it up and
/// prints to the physical ESC/POS printer on the shop's LAN.
/// </summary>
public interface IPrintDispatcher
{
    Task Dispatch(string slug, PrintJob job, CancellationToken ct = default);
}
