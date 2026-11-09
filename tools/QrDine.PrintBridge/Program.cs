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
// The print feed carries every bill, so the bridge signs in like a staff member. Make a staff
// account for the counter PC (Settings → Staff), then run it with that account's sign-in name.
// The password comes from QRDINE_PASSWORD, or it asks for it (nothing shows as you type).
//
// Usage: QrDine.PrintBridge [apiBaseUrl] [mobileOrUsername]
//   default address: http://localhost:5099

var apiBase = (args.Length > 0 ? args[0] : "http://localhost:5099").TrimEnd('/');
var username = args.Length > 1 ? args[1] : Ask("Staff mobile number or username: ", hidden: false);
var password = Environment.GetEnvironmentVariable("QRDINE_PASSWORD") ?? Ask("Password: ", hidden: true);
var outDir = Path.Combine(AppContext.BaseDirectory, "printouts");
Directory.CreateDirectory(outDir);
Console.OutputEncoding = Encoding.UTF8;

using var http = new HttpClient { BaseAddress = new Uri(apiBase + "/") };
var (token, slug) = await SignIn(http, username, password);
var tokenTakenAt = DateTime.UtcNow;

Console.WriteLine($"QrDine Print Bridge → {apiBase}  (restaurant: {slug})");
Console.WriteLine($"Printouts saved to: {outDir}");
Console.WriteLine("Waiting for print jobs… (Ctrl+C to quit)\n");

var conn = new HubConnectionBuilder()
    .WithUrl($"{apiBase}/hub/orders", o => o.AccessTokenProvider = async () =>
    {
        // A sign-in lasts 12 hours; sign in again a little before that, on the next reconnect.
        if (DateTime.UtcNow - tokenTakenAt > TimeSpan.FromHours(11))
        {
            (token, slug) = await SignIn(http, username, password);
            tokenTakenAt = DateTime.UtcNow;
        }
        return token;
    })
    .WithAutomaticReconnect()
    .Build();
conn.Closed += async _ =>
{
    // The server ends a connection when its sign-in runs out; come back with a fresh one.
    await Task.Delay(3000);
    tokenTakenAt = DateTime.MinValue;
    await ConnectWithRetry(conn);
    await conn.InvokeAsync("JoinPrinters", slug);
    Console.WriteLine("[connected again]");
};

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
static async Task<(string Token, string Slug)> SignIn(HttpClient http, string username, string password)
{
    while (true)
    {
        try
        {
            using var res = await http.PostAsync("api/auth/login",
                new StringContent(JsonSerializer.Serialize(new { username, password }), Encoding.UTF8, "application/json"));
            var body = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                return (doc.RootElement.GetProperty("token").GetString()!, doc.RootElement.GetProperty("slug").GetString()!);
            }
            if ((int)res.StatusCode is 400 or 401 or 403)
            {
                Console.WriteLine($"Sign-in refused ({(int)res.StatusCode}). Check the name and password, then start the bridge again.");
                Environment.Exit(1);
            }
            Console.WriteLine($"Sign-in failed ({(int)res.StatusCode}), retrying in 10s…");
        }
        catch (HttpRequestException) { Console.WriteLine("API not reachable, retrying in 10s…"); }
        await Task.Delay(10_000);
    }
}

static string Ask(string prompt, bool hidden)
{
    Console.Write(prompt);
    if (!hidden || Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var sb = new StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
}

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
