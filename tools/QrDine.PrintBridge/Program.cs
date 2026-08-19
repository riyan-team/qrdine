using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

// QrDine Print Bridge
// -------------------
// A tiny on-site app that runs on the restaurant's counter PC. It connects to the
// QrDine API over SignalR, joins the restaurant's "printers" group, and prints the
// jobs it receives (kitchen tickets, bills) to the ESC/POS printer on the LAN.
//
// This build "prints" to the console and to ./printouts/*.txt so the pipeline is
// fully testable without hardware. The one hardware-specific step — sending ESC/POS
// bytes to a real printer — is isolated in EscPos.Send() below and clearly marked.
//
// Usage: QrDine.PrintBridge [apiBaseUrl] [restaurantSlug]
//   defaults: http://localhost:5099   desidhaba

var apiBase = args.Length > 0 ? args[0] : "http://localhost:5099";
var slug = args.Length > 1 ? args[1] : "desidhaba";
var outDir = Path.Combine(AppContext.BaseDirectory, "printouts");
Directory.CreateDirectory(outDir);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"QrDine Print Bridge → {apiBase}  (restaurant: {slug})");
Console.WriteLine($"Printouts saved to: {outDir}");
Console.WriteLine("Waiting for print jobs… (Ctrl+C to quit)\n");

var conn = new HubConnectionBuilder()
    .WithUrl($"{apiBase}/hub/orders")
    .WithAutomaticReconnect()
    .Build();

conn.On<JsonElement>("print", async job =>
{
    try
    {
        var type = job.GetProperty("type").GetString() ?? "job";
        var title = job.GetProperty("title").GetString() ?? "";
        var lines = job.TryGetProperty("lines", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
        var qr = job.TryGetProperty("qrPayload", out var q) ? q.GetString() : null;
        var footer = job.TryGetProperty("footer", out var f) ? f.GetString() : null;

        var text = Render(type, title, lines, qr, footer);
        Console.WriteLine(text);

        var file = Path.Combine(outDir, $"{type}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
        await File.WriteAllTextAsync(file, text);

        // Real hardware: uncomment to send to an 80mm ESC/POS printer.
        // EscPos.Send(type, title, lines, qr, footer);
    }
    catch (Exception ex) { Console.WriteLine($"[print error] {ex.Message}"); }
});

conn.Reconnected += async _ => { await conn.InvokeAsync("JoinPrinters", slug); Console.WriteLine("[reconnected]"); };

await ConnectWithRetry(conn);
await conn.InvokeAsync("JoinPrinters", slug);
Console.WriteLine("[connected — joined printers group]\n");

await Task.Delay(Timeout.Infinite);

// ---- helpers ----
static async Task ConnectWithRetry(HubConnection conn)
{
    while (true)
    {
        try { await conn.StartAsync(); return; }
        catch { Console.WriteLine("API not reachable, retrying in 3s…"); await Task.Delay(3000); }
    }
}

static string Render(string type, string title, string[] lines, string? qr, string? footer)
{
    var sb = new StringBuilder();
    sb.AppendLine("========== 80mm ==========");
    sb.AppendLine(Center(title.ToUpperInvariant()));
    sb.AppendLine();
    foreach (var line in lines) sb.AppendLine(line);
    if (!string.IsNullOrWhiteSpace(qr)) { sb.AppendLine(); sb.AppendLine("[QR] " + qr); }
    if (!string.IsNullOrWhiteSpace(footer)) { sb.AppendLine(); sb.AppendLine(Center(footer!)); }
    sb.AppendLine("==========================");
    return sb.ToString();
}

static string Center(string s)
{
    const int w = 32;
    if (s.Length >= w) return s;
    var pad = (w - s.Length) / 2;
    return new string(' ', pad) + s;
}
