using System.Text.Json.Serialization;
using QrDine.Application.Billing;
using QrDine.Domain.Billing;

namespace QrDine.Infrastructure.Fiscal;

/// <summary>
/// The exact invoice model FBR's fiscalization component expects (field names and
/// casing match the FBR "Data Sharing" spec). Both the Local IMS component and the
/// Cloud gateway accept this same JSON shape.
/// </summary>
public class FbrInvoiceModel
{
    [JsonPropertyName("InvoiceNumber")] public string InvoiceNumber { get; set; } = "";
    [JsonPropertyName("POSID")] public long POSID { get; set; }
    [JsonPropertyName("USIN")] public string USIN { get; set; } = "";
    [JsonPropertyName("DateTime")] public string DateTime { get; set; } = "";
    [JsonPropertyName("BuyerNTN")] public string? BuyerNTN { get; set; }
    [JsonPropertyName("BuyerCNIC")] public string? BuyerCNIC { get; set; }
    [JsonPropertyName("BuyerName")] public string? BuyerName { get; set; }
    [JsonPropertyName("BuyerPhoneNumber")] public string? BuyerPhoneNumber { get; set; }
    [JsonPropertyName("TotalBillAmount")] public double TotalBillAmount { get; set; }
    [JsonPropertyName("TotalQuantity")] public double TotalQuantity { get; set; }
    [JsonPropertyName("TotalSaleValue")] public double TotalSaleValue { get; set; }
    [JsonPropertyName("TotalTaxCharged")] public double TotalTaxCharged { get; set; }
    [JsonPropertyName("Discount")] public double Discount { get; set; }
    [JsonPropertyName("FurtherTax")] public double FurtherTax { get; set; }
    [JsonPropertyName("PaymentMode")] public int PaymentMode { get; set; }
    [JsonPropertyName("RefUSIN")] public string? RefUSIN { get; set; }
    [JsonPropertyName("InvoiceType")] public int InvoiceType { get; set; } = 1;
    [JsonPropertyName("Items")] public List<FbrInvoiceItem> Items { get; set; } = new();
}

public class FbrInvoiceItem
{
    [JsonPropertyName("ItemCode")] public string ItemCode { get; set; } = "";
    [JsonPropertyName("ItemName")] public string ItemName { get; set; } = "";
    [JsonPropertyName("Quantity")] public double Quantity { get; set; }
    [JsonPropertyName("PCTCode")] public string PCTCode { get; set; } = "";
    [JsonPropertyName("TaxRate")] public double TaxRate { get; set; }
    [JsonPropertyName("SaleValue")] public double SaleValue { get; set; }
    [JsonPropertyName("TotalAmount")] public double TotalAmount { get; set; }
    [JsonPropertyName("TaxCharged")] public double TaxCharged { get; set; }
    [JsonPropertyName("Discount")] public double Discount { get; set; }
    [JsonPropertyName("FurtherTax")] public double FurtherTax { get; set; }
    [JsonPropertyName("InvoiceType")] public int InvoiceType { get; set; } = 1;
    [JsonPropertyName("RefUSIN")] public string? RefUSIN { get; set; }
}

/// <summary>FBR's response shape. The IMS component and the cloud gateway differ
/// slightly (InvoiceNumber vs FBRInvoiceNumber), so we accept both.</summary>
public class FbrResponse
{
    [JsonPropertyName("InvoiceNumber")] public string? InvoiceNumber { get; set; }
    [JsonPropertyName("FBRInvoiceNumber")] public string? FBRInvoiceNumber { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("Response")] public string? Response { get; set; }
    [JsonPropertyName("Errors")] public string? Errors { get; set; }

    public string? Number => !string.IsNullOrWhiteSpace(FBRInvoiceNumber) ? FBRInvoiceNumber : InvoiceNumber;
    public bool Accepted => Code == "100" && !string.IsNullOrWhiteSpace(Number);
}

/// <summary>Maps our settled <see cref="Invoice"/> to the FBR invoice model.</summary>
public static class FbrInvoiceMapper
{
    public static FbrInvoiceModel Map(Invoice inv, FiscalContext ctx)
    {
        var items = inv.Lines.Select(l => new FbrInvoiceItem
        {
            ItemCode = string.IsNullOrWhiteSpace(l.ItemCode) ? "ITEM" : l.ItemCode,
            ItemName = l.Name,
            Quantity = (double)l.Qty,
            PCTCode = l.PctCode,
            TaxRate = (double)l.TaxRate,
            SaleValue = (double)l.SaleValue,
            TaxCharged = (double)l.TaxAmount,
            Discount = (double)l.DiscountAmount,
            FurtherTax = (double)l.FurtherTaxAmount,
            TotalAmount = (double)l.TotalAmount,
            InvoiceType = inv.InvoiceType,
        }).ToList();

        // Service charge isn't an FBR field — carry it as a zero-tax line so the
        // item totals reconcile with TotalBillAmount.
        if (inv.ServiceChargeAmount > 0)
            items.Add(new FbrInvoiceItem
            {
                ItemCode = "SERVICE", ItemName = "Service Charge", Quantity = 1,
                PCTCode = "", TaxRate = 0, SaleValue = (double)inv.ServiceChargeAmount,
                TaxCharged = 0, Discount = 0, FurtherTax = 0,
                TotalAmount = (double)inv.ServiceChargeAmount, InvoiceType = inv.InvoiceType,
            });

        return new FbrInvoiceModel
        {
            InvoiceNumber = "",
            POSID = ctx.PosId ?? 0,
            USIN = inv.Usin,
            DateTime = inv.IssuedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            BuyerNTN = NullIfBlank(inv.BuyerNtn),
            BuyerCNIC = NullIfBlank(inv.BuyerCnic),
            BuyerName = NullIfBlank(inv.BuyerName),
            BuyerPhoneNumber = NullIfBlank(inv.BuyerPhone),
            PaymentMode = PaymentMode(inv.PaymentMethod),
            InvoiceType = inv.InvoiceType,
            RefUSIN = null,
            Items = items,
            TotalQuantity = items.Sum(i => i.Quantity),
            TotalSaleValue = items.Sum(i => i.SaleValue),
            TotalTaxCharged = items.Sum(i => i.TaxCharged),
            Discount = items.Sum(i => i.Discount),
            FurtherTax = items.Sum(i => i.FurtherTax),
            TotalBillAmount = items.Sum(i => i.TotalAmount),
        };
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // FBR PaymentMode codes: 1 Cash, 2 Card, 3 Gift Voucher, 4 Loyalty, 5 Mixed, 6 Cheque.
    public static int PaymentMode(string method)
    {
        var m = (method ?? "").Trim().ToLowerInvariant();
        if (m.StartsWith("split") || m.Contains('+') || m.Contains("mixed")) return 5;
        return m switch
        {
            "cash" => 1,
            "card" or "credit" or "debit" or "jazzcash" or "easypaisa" or "raast" => 2,
            "gift" or "gift voucher" => 3,
            "loyalty" or "loyalty card" => 4,
            "cheque" or "check" => 6,
            _ => 1,
        };
    }
}
