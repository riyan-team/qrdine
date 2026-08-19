namespace QrDine.Infrastructure.Payments;

/// <summary>Bound from the "Payments" config section.</summary>
public class PaymentOptions
{
    /// <summary>Mock | JazzCash</summary>
    public string Provider { get; set; } = "Mock";

    // JazzCash merchant credentials (from your JazzCash integration kit).
    public string? MerchantId { get; set; }
    public string? Password { get; set; }
    public string? IntegritySalt { get; set; }
    public string ReturnUrl { get; set; } = "http://localhost:5173/pay/return";
    public string PostUrl { get; set; } = "https://sandbox.jazzcash.com.pk/CustomerPortal/transactionmanagement/merchantform/";
}
