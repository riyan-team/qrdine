using QrDine.Application.Billing;
using QrDine.Application.Ordering.Dtos;

namespace QrDine.Application.Printing;

/// <summary>
/// A printer-agnostic print job. The bridge renders Title + Lines as text (and
/// the QrPayload as a QR) — so the same job prints on any 80mm ESC/POS printer
/// without the server knowing anything about the hardware.
/// </summary>
public record PrintJob(string Type, string Title, IReadOnlyList<string> Lines, string? QrPayload, string? Footer);

/// <summary>Builds print jobs from domain DTOs. Pure formatting — no I/O.</summary>
public static class PrintJobs
{
    public static PrintJob KitchenTicket(OrderDto o)
    {
        var lines = new List<string> { $"Table {o.TableCode}  ·  Order #{o.Id}", new string('-', 32) };
        foreach (var i in o.Items)
        {
            var name = i.VariantName is null ? i.Name : $"{i.Name} ({i.VariantName})";
            lines.Add($"{i.Qty} x {name}");
            if (i.Modifiers.Count > 0) lines.Add($"    + {string.Join(", ", i.Modifiers)}");
            if (!string.IsNullOrWhiteSpace(i.Note)) lines.Add($"    note: {i.Note}");
        }
        if (!string.IsNullOrWhiteSpace(o.CustomerNote)) lines.Add($"** {o.CustomerNote} **");
        return new PrintJob("kitchen", $"KITCHEN  ·  {o.TableName}", lines, null, null);
    }

    public static PrintJob Receipt(InvoiceDto inv)
    {
        string Money(decimal v) => $"{inv.Currency} {v:N0}";
        var lines = new List<string> { inv.TableName, Common.BusinessDay.ToPkt(inv.IssuedAt).ToString("dd-MMM-yyyy HH:mm"), new string('-', 32) };
        foreach (var l in inv.Lines)
            lines.Add($"{l.Qty} x {l.Name}".PadRight(24) + Money(l.LineTotal).PadLeft(8));
        lines.Add(new string('-', 32));
        lines.Add("Subtotal".PadRight(24) + Money(inv.Subtotal).PadLeft(8));
        if (inv.ServiceChargePercent > 0) lines.Add($"Service {inv.ServiceChargePercent}%".PadRight(24) + Money(inv.ServiceChargeAmount).PadLeft(8));
        if (inv.ServiceTaxAmount > 0) lines.Add("Tax on service".PadRight(24) + Money(inv.ServiceTaxAmount).PadLeft(8));
        if (inv.TaxPercent > 0) lines.Add($"GST {inv.TaxPercent}%".PadRight(24) + Money(inv.TaxAmount).PadLeft(8));
        if (inv.FurtherTaxAmount > 0) lines.Add("Further tax".PadRight(24) + Money(inv.FurtherTaxAmount).PadLeft(8));
        if (inv.DiscountAmount > 0) lines.Add("Discount".PadRight(24) + ("-" + Money(inv.DiscountAmount)).PadLeft(8));
        if (inv.RoundingAmount != 0) lines.Add("Rounding".PadRight(24) + $"{inv.Currency} {inv.RoundingAmount:0.00}".PadLeft(8));
        lines.Add("TOTAL".PadRight(24) + Money(inv.Total).PadLeft(8));
        lines.Add($"Paid via {inv.PaymentMethod}");

        // Fiscal footer. The receipt always prints; only this section reflects the FBR
        // state, so a failed/offline filing never blocks the print.
        string? qr = null;
        var office = inv.FiscalOffice;
        if (!string.IsNullOrWhiteSpace(inv.FiscalNumber))
        {
            lines.Add(new string('-', 32));
            if (inv.FiscalEnvironment == "Practice") lines.Add("*** PRACTICE - NOT A TAX INVOICE ***");
            else if (inv.FiscalEnvironment == "Test") lines.Add($"*** {office} TEST - NOT A TAX INVOICE ***");
            else lines.Add($"*** INTEGRATED WITH {office} ***");
            if (!string.IsNullOrWhiteSpace(inv.SellerNtn)) lines.Add($"NTN: {inv.SellerNtn}");
            if (!string.IsNullOrWhiteSpace(inv.SellerStrn)) lines.Add($"STRN: {inv.SellerStrn}");
            if (inv.PosId is long pos) lines.Add($"POS ID: {pos}");
            lines.Add($"{office} Invoice: {inv.FiscalNumber}");
            lines.Add(office == "PRA" ? "Scan to check it on PRA's website" : "Verify in the FBR Tax Asaan app");
            qr = inv.FiscalQrPayload ?? inv.FiscalNumber;
        }
        else if (inv.FiscalStatus is "Pending" or "Failed")
        {
            lines.Add(new string('-', 32));
            lines.Add($"{office} filing pending - sale is valid,");
            lines.Add($"will be filed with {office} shortly.");
        }
        return new PrintJob("receipt", inv.RestaurantName, lines, qr, "Thank you — please come again!");
    }
}
