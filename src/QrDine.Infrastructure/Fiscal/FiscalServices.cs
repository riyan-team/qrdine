using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QrDine.Application.Billing;
using QrDine.Domain.Billing;
using QrDine.Domain.Restaurants;

namespace QrDine.Infrastructure.Fiscal;

/// <summary>
/// Simulates FBR registration so the whole flow (fiscal number + scannable QR on
/// the receipt, synced/pending views) is testable without a live FBR connection.
/// Used when Fiscal:Simulate = true (dev/demo default).
/// </summary>
public class SimulatedFiscalService : IFiscalService
{
    public Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default)
    {
        // A registered POS is still required for a real filing — surface a clear error
        // in the pending list if it's missing, so the settings gap is obvious.
        if (ctx.PosId is null or 0)
            return Task.FromResult(new FiscalResult("Simulated", FiscalStatus.Failed, null, null,
                "No POSID configured for this branch/operator. Set it in Settings → FBR."));

        var stamp = invoice.IssuedAt.ToString("yyyyMMddHHmmss");
        var number = $"{ctx.PosId}{stamp}{invoice.Number:D5}";   // stands in for the FBR IRN
        return Task.FromResult(new FiscalResult("Simulated", FiscalStatus.Registered, number, number, null));
    }
}

/// <summary>
/// Real FBR "Digital Invoicing" transport. Local mode POSTs to the IMS component on
/// the POS PC (http://localhost:8524); Cloud mode POSTs to esp/gw.fbr.gov.pk with a
/// bearer token. A network failure returns Pending (sale keeps, retry later); an FBR
/// rejection returns Failed with the authority's message.
/// </summary>
public class FbrFiscalService : IFiscalService
{
    private readonly HttpClient _http;
    private readonly FiscalOptions _opt;
    private readonly ILogger<FbrFiscalService> _log;

    public FbrFiscalService(HttpClient http, IOptions<FiscalOptions> opt, ILogger<FbrFiscalService> log)
    {
        _http = http; _opt = opt.Value; _log = log;
    }

    public async Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default)
    {
        const string provider = "FBR";
        if (ctx.PosId is null or 0)
            return new FiscalResult(provider, FiscalStatus.Failed, null, null,
                "No POSID configured for this branch/operator. Set it in Settings → FBR.");

        var url = !string.IsNullOrWhiteSpace(ctx.EndpointOverride)
            ? ctx.EndpointOverride!
            : ctx.Mode == FbrMode.Local
                ? _opt.LocalUrl
                : ctx.Sandbox ? _opt.CloudSandboxUrl : _opt.CloudProductionUrl;

        if (ctx.Mode == FbrMode.Cloud && string.IsNullOrWhiteSpace(ctx.Token))
            return new FiscalResult(provider, FiscalStatus.Failed, null, null,
                "No FBR bearer token configured for cloud mode. Set it in Settings → FBR.");

        var payload = FbrInvoiceMapper.Map(invoice, ctx);

        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            if (ctx.Mode == FbrMode.Cloud)
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

            using var resp = await _http.SendAsync(msg, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("FBR {Status} for invoice {Number}: {Body}", (int)resp.StatusCode, invoice.FormattedNumber, body);
                // Transport reachable but rejected → Failed (retry after fixing data/creds).
                return new FiscalResult(provider, FiscalStatus.Failed, null, null,
                    $"FBR HTTP {(int)resp.StatusCode}: {Trim(body)}");
            }

            var fbr = System.Text.Json.JsonSerializer.Deserialize<FbrResponse>(body);
            if (fbr is null)
                return new FiscalResult(provider, FiscalStatus.Failed, null, null, $"Unreadable FBR response: {Trim(body)}");
            if (!fbr.Accepted)
                return new FiscalResult(provider, FiscalStatus.Failed, null, null,
                    $"FBR rejected (code {fbr.Code}): {fbr.Errors ?? fbr.Response ?? Trim(body)}");

            return new FiscalResult(provider, FiscalStatus.Registered, fbr.Number, fbr.Number, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Couldn't reach FBR — keep the sale, leave it Pending for a manual retry.
            _log.LogWarning(ex, "FBR unreachable for invoice {Number}; left Pending.", invoice.FormattedNumber);
            return new FiscalResult(provider, FiscalStatus.Pending, null, null,
                $"Could not reach FBR ({ctx.Mode}): {ex.Message}");
        }
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] : s;
}
