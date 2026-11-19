namespace QrDine.Application.Admin;

/// <summary>
/// The limits on the bill settings, said the same way by the form check and by the service,
/// so a wrong number is refused with a clear reason instead of being quietly changed.
/// </summary>
public static class BillingRules
{
    public const decimal MaxAmount = 100_000_000m;   // the money columns hold up to 99,999,999.99

    public const string TaxMessage = "Tax % must be between 0 and 100.";
    public const string CardTaxMessage = "Card tax % must be between 0 and 100.";
    public const string ServiceMessage = "Service charge % must be between 0 and 100.";
    public const string DiscountNegativeMessage = "Discount can't be negative.";
    public const string DiscountPercentMessage = "Discount % must be between 0 and 100.";
    public const string DiscountTooBigMessage = "That discount is too big.";
    public const string ItemTaxMessage = "Item tax % must be between 0 and 100.";
    public const string FurtherTaxMessage = "Further tax % must be between 0 and 100.";

    public static bool IsPercent(decimal value) => value is >= 0 and <= 100;
}
