using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QrDine.Application.Billing;
using QrDine.Domain.Billing;
using QrDine.Domain.Restaurants;

namespace QrDine.Infrastructure.Fiscal;

/// <summary>
/// Practice filing: a made-up number in the tax office's format and a scannable QR, so the whole flow (receipt,
/// Pending and Filed lists) can be learned without sending anything. Receipts say PRACTICE.
/// </summary>
public class SimulatedFiscalService : IFiscalService
{
    public Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default)
    {
        var office = ctx.Authority.ToString();
        var stamp = invoice.IssuedAt.ToString("yyyyMMddHHmmss");
        var number = $"PRACTICE-{ctx.PosId ?? 0}{stamp}{invoice.Number:D5}";
        return Task.FromResult(new FiscalResult(office, FiscalStatus.Registered, number, number, null,
            Environment: nameof(FiscalEnvironment.Practice)));
    }
}

/// <summary>What this install can do, from the "Fiscal" settings.</summary>
public sealed class FiscalCapabilities(IOptions<FiscalOptions> opt) : IFiscalCapabilities
{
    public bool ForcedPractice => opt.Value.Simulate;
    public bool LocalAvailable => opt.Value.AllowLocal;
}

/// <summary>
/// Sends a bill to FBR or PRA.
/// - Practice (or a server that forces practice): handled by <see cref="SimulatedFiscalService"/>, nothing sent.
/// - FBR online: esp.fbr.gov.pk (Test) or gw.fbr.gov.pk (Live), with the branch's bearer token.
/// - FBR on this computer: the FBR IMS software at localhost:8524 (PC edition only).
/// - PRA: ims.pral.com.pk sandbox (Test) or production (Live), with the token; the same JSON as FBR except the
///   buyer's NTN is "BuyerPNTN", and the receipt's QR opens PRA's page for the invoice.
/// A network failure leaves the bill Pending (it is sent later); a refusal marks it Failed with the office's reason.
/// </summary>
public class FbrFiscalService : IFiscalService
{
    private readonly HttpClient _http;
    private readonly FiscalOptions _opt;
    private readonly ILogger<FbrFiscalService> _log;
    private readonly SimulatedFiscalService _practice = new();

    public FbrFiscalService(HttpClient http, IOptions<FiscalOptions> opt, ILogger<FbrFiscalService> log)
    {
        _http = http; _opt = opt.Value; _log = log;
    }

    public async Task<FiscalResult> Register(Invoice invoice, FiscalContext ctx, CancellationToken ct = default)
    {
        if (_opt.Simulate || ctx.Environment == FiscalEnvironment.Practice)
        {
            var practice = await _practice.Register(invoice, ctx, ct);
            // The log shows the JSON a real send would carry, so practice teaches what goes to the office.
            return practice with { Exchange = new FiscalExchange("Practice: nothing was sent", TryPayload(invoice, ctx), null, null, 0) };
        }

        var office = ctx.Authority.ToString();
        var env = ctx.Environment.ToString();
        var url = UrlFor(ctx);
        FiscalResult Refused(string why, FiscalExchange? x = null) =>
            new(office, FiscalStatus.Failed, null, null, why, env) { Exchange = x ?? new FiscalExchange($"Not sent: {url}", null, null, null, 0) };

        if (ctx.PosId is null or <= 0)
            return Refused($"No POSID for this branch or cashier. Add the POSID {office} gave you in Settings → Tax office.");
        var local = ctx.Authority == FiscalAuthority.FBR && ctx.Mode == FbrMode.Local;
        if (local && !_opt.AllowLocal)
            return Refused("“On this computer” needs the FBR software on the counter PC (PC edition). On the online system, choose “Online” for this branch.");
        if (!local && string.IsNullOrWhiteSpace(ctx.Token))
            return Refused($"No {office} token for this branch. Add it in Settings → Tax office.");

        var json = Payload(invoice, ctx);
        var clock = Stopwatch.StartNew();
        FiscalExchange Sent(int? status, string? body) => new(url, json, status, body, (int)clock.ElapsedMilliseconds);
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (!local) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token!.Trim());

            using var resp = await _http.SendAsync(msg, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var x = Sent((int)resp.StatusCode, body);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("{Office} {Env} HTTP {Status} for invoice {Number}: {Body}", office, env, (int)resp.StatusCode, invoice.FormattedNumber, Trim(body));
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || body.Contains("900908"))
                    return Refused(NotAllowed(office, env), x);
                // The office's own server failing (5xx, gateway timeouts) is temporary: keep the bill waiting.
                if ((int)resp.StatusCode >= 500)
                    return new FiscalResult(office, FiscalStatus.Pending, null, null,
                        $"{office} {env} is having a problem (HTTP {(int)resp.StatusCode}). The bill will be sent again.", env, Unreachable: true) { Exchange = x };
                return Refused($"{office} refused it (HTTP {(int)resp.StatusCode}): {Trim(body)}", x);
            }

            FbrResponse? answer = null;
            try { answer = JsonSerializer.Deserialize<FbrResponse>(body); } catch (JsonException) { }
            if (answer is null)
                return Refused($"{office} sent an answer that couldn't be read: {Trim(body)}", x);
            if (!answer.Accepted)
                return Refused($"{office} refused it (code {answer.Code}): {answer.Errors ?? answer.Response ?? Trim(body)}", x);

            var qr = ctx.Authority == FiscalAuthority.PRA ? string.Format(_opt.PraVerifyUrl, Uri.EscapeDataString(answer.Number!)) : answer.Number;
            return new FiscalResult(office, FiscalStatus.Registered, answer.Number, qr, null, env) { Exchange = x };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "{Office} {Env} unreachable for invoice {Number}; left waiting.", office, env, invoice.FormattedNumber);
            return new FiscalResult(office, FiscalStatus.Pending, null, null,
                $"Couldn't reach {office} ({(local ? "FBR software on this PC" : env)}): {ex.Message}. The bill will be sent again.", env, Unreachable: true)
            { Exchange = Sent(null, null) };
        }
    }

    // Practice never fails over the log: a bill the mapper can't turn into JSON still gets its practice number.
    private static string? TryPayload(Invoice invoice, FiscalContext ctx)
    {
        try { return Payload(invoice, ctx); }
        catch (Exception) { return null; }
    }

    private static string NotAllowed(string office, string env) =>
        $"{office} didn't accept the token, or this server's internet address isn't on {office}'s list. Check the {env} token in " +
        $"Settings → Tax office, and ask {office} to add the address shown there.";

    /// <summary>Where this bill goes: a branch's own address wins (for a test server), else the office's.</summary>
    public string UrlFor(FiscalContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.EndpointOverride)) return ctx.EndpointOverride!.Trim();
        var live = ctx.Environment == FiscalEnvironment.Live;
        if (ctx.Authority == FiscalAuthority.PRA) return live ? _opt.PraProductionUrl : _opt.PraSandboxUrl;
        if (ctx.Mode == FbrMode.Local) return _opt.LocalUrl;
        return live ? _opt.CloudProductionUrl : _opt.CloudSandboxUrl;
    }

    /// <summary>The JSON sent: FBR's invoice model, with PRA's two differences.</summary>
    public static string Payload(Invoice invoice, FiscalContext ctx)
    {
        var model = FbrInvoiceMapper.Map(invoice, ctx);
        if (ctx.Authority != FiscalAuthority.PRA) return JsonSerializer.Serialize(model);
        model.DateTime = Application.Common.BusinessDay.ToPkt(invoice.IssuedAt).ToString("yyyy-MM-dd HH:mm:ss.fff");
        var node = JsonSerializer.SerializeToNode(model)!.AsObject();
        node["BuyerPNTN"] = node["BuyerNTN"]?.DeepClone();
        node.Remove("BuyerNTN");
        return node.ToJsonString();
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] : s;
}
