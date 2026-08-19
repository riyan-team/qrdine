using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QrDine.Application.Common;

namespace QrDine.Infrastructure.Payments;

/// <summary>Dev gateway — always succeeds, no real charge.</summary>
public class MockPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> CreateCheckout(PaymentRequest req, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult("Mock", "paid", req.Reference, Message: "Demo payment — no real charge."));
}

/// <summary>
/// JazzCash "Page Redirection" (hosted checkout). Builds the pp_* fields and the
/// HMAC-SHA256 secure hash exactly as JazzCash expects, then returns them for the
/// browser to POST to JazzCash. Goes live the moment MerchantId / Password /
/// IntegritySalt are set — until then it reports "unconfigured".
/// </summary>
public class JazzCashPaymentGateway : IPaymentGateway
{
    private readonly PaymentOptions _o;
    private readonly ILogger<JazzCashPaymentGateway> _log;
    public JazzCashPaymentGateway(IOptions<PaymentOptions> o, ILogger<JazzCashPaymentGateway> log) { _o = o.Value; _log = log; }

    public Task<PaymentResult> CreateCheckout(PaymentRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_o.MerchantId) || string.IsNullOrWhiteSpace(_o.Password) || string.IsNullOrWhiteSpace(_o.IntegritySalt))
        {
            _log.LogWarning("JazzCash not configured (MerchantId/Password/IntegritySalt missing).");
            return Task.FromResult(new PaymentResult("JazzCash", "unconfigured", req.Reference,
                Message: "Add your JazzCash merchant credentials to go live."));
        }

        var nowPkt = DateTime.UtcNow.AddHours(5);
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["pp_Version"] = "1.1",
            ["pp_TxnType"] = "MWALLET",
            ["pp_Language"] = "EN",
            ["pp_MerchantID"] = _o.MerchantId!,
            ["pp_SubMerchantID"] = "",
            ["pp_Password"] = _o.Password!,
            ["pp_TxnRefNo"] = "T" + nowPkt.ToString("yyyyMMddHHmmss") + Random.Shared.Next(100, 999),
            ["pp_Amount"] = ((long)(req.AmountPkr * 100)).ToString(),   // paisa
            ["pp_TxnCurrency"] = "PKR",
            ["pp_TxnDateTime"] = nowPkt.ToString("yyyyMMddHHmmss"),
            ["pp_BillReference"] = req.Reference,
            ["pp_Description"] = req.Description,
            ["pp_TxnExpiryDateTime"] = nowPkt.AddHours(1).ToString("yyyyMMddHHmmss"),
            ["pp_ReturnURL"] = _o.ReturnUrl,
            ["ppmpf_1"] = req.Reference,
        };

        fields["pp_SecureHash"] = ComputeHash(fields, _o.IntegritySalt!);

        return Task.FromResult(new PaymentResult("JazzCash", "redirect", req.Reference,
            RedirectUrl: _o.PostUrl, Fields: fields));
    }

    // HMAC-SHA256 over the integrity salt + '&'-joined non-empty field values (sorted by key).
    private static string ComputeHash(IDictionary<string, string> fields, string salt)
    {
        var values = fields.Where(kv => kv.Key != "pp_SecureHash" && !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value);
        var message = salt + "&" + string.Join("&", values);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash);
    }
}
