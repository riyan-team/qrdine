using QrDine.Domain.Billing;

namespace QrDine.Application.Billing;

public record BillLineDto(string Name, int Qty, decimal UnitPrice, decimal LineTotal);

/// <summary>A live, unsettled bill preview for a table.</summary>
public record BillDto(
    string RestaurantName, string Currency, string TableCode, string TableName,
    IReadOnlyList<BillLineDto> Lines,
    decimal Subtotal, decimal ServiceChargePercent, decimal ServiceChargeAmount,
    decimal TaxPercent, decimal TaxAmount, decimal Total, bool HasOpenOrders,
    decimal AmountPaid, decimal Remaining,
    string LogoUrl = "", string ReceiptHeader = "", string ReceiptFooter = "");

public record SettleRequest(string Method, decimal Discount);
public record PayPartRequest(decimal Amount, string Method);
public record PayPartResult(bool Settled, decimal Paid, decimal Remaining, InvoiceDto? Invoice);

/// <summary>A row in the owner's FBR view — one settled invoice and its fiscal state.</summary>
public record FiscalInvoiceDto(
    int Id, string Number, string Usin, string TableName, DateTime IssuedAt, string Currency,
    decimal Total, string PaymentMethod, string FiscalStatus, string? FiscalNumber,
    string? FiscalError, int FiscalAttempts, DateTime? FiscalLastAttemptAt, long? SyncedByPosId);

/// <summary>A settled, numbered invoice (also used for reprint).</summary>
public record InvoiceDto(
    int Id, string Number, string RestaurantName, string Currency,
    string TableCode, string TableName, DateTime IssuedAt, string PaymentMethod,
    IReadOnlyList<BillLineDto> Lines,
    decimal Subtotal, decimal ServiceChargePercent, decimal ServiceChargeAmount,
    decimal TaxPercent, decimal TaxAmount, decimal DiscountAmount, decimal Total,
    string FiscalProvider, string FiscalStatus, string? FiscalNumber, string? FiscalQrPayload,
    string? FiscalError,
    string LogoUrl = "", string ReceiptHeader = "", string ReceiptFooter = "")
{
    public static InvoiceDto From(Invoice inv, string restaurantName, string currency,
        string logoUrl = "", string receiptHeader = "", string receiptFooter = "") => new(
        inv.Id, inv.FormattedNumber, restaurantName, currency, inv.TableCode, inv.TableName,
        inv.IssuedAt, inv.PaymentMethod,
        inv.Lines.Select(l => new BillLineDto(l.Name, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
        inv.Subtotal, inv.ServiceChargePercent, inv.ServiceChargeAmount,
        inv.TaxPercent, inv.TaxAmount, inv.DiscountAmount, inv.Total,
        inv.FiscalProvider, inv.FiscalStatus.ToString(), inv.FiscalNumber, inv.FiscalQrPayload,
        inv.FiscalError, logoUrl, receiptHeader, receiptFooter);
}
