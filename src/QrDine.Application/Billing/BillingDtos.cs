using QrDine.Domain.Billing;

namespace QrDine.Application.Billing;

public record BillLineDto(string Name, int Qty, decimal UnitPrice, decimal LineTotal);

/// <summary>A live, unsettled bill preview for a table. Reflects the current
/// tax/discount settings so the cashier screen shows exactly what will be charged.</summary>
public record BillDto(
    string RestaurantName, string Currency, string TableCode, string TableName,
    IReadOnlyList<BillLineDto> Lines,
    decimal Subtotal, decimal ServiceChargePercent, decimal ServiceChargeAmount,
    decimal TaxPercent, decimal TaxAmount, decimal Total, bool HasOpenOrders,
    decimal AmountPaid, decimal Remaining,
    bool TaxEnabled, string TaxName, bool DiscountEnabled, decimal DiscountAmount,
    bool ReceiptEnabled,
    string LogoUrl = "", string ReceiptHeader = "", string ReceiptFooter = "");

// Discount is the applied Rs amount the cashier confirmed (pre-filled from settings);
// AmountTendered is the cash received (used to compute & store the change).
public record SettleRequest(string Method, decimal Discount, decimal AmountTendered = 0);
public record PayPartRequest(decimal Amount, string Method);
public record PayPartResult(bool Settled, decimal Paid, decimal Remaining, InvoiceDto? Invoice);

// ---- Reception dashboard ----
/// <summary>An open (unsettled) table with its running total, shown as a pending bill.</summary>
public record OpenTableDto(string TableCode, string TableName, int Orders, decimal Amount, DateTime OpenedAt);

/// <summary>One settled bill in the reception history list.</summary>
public record SettledBillDto(int Id, string Number, DateTime IssuedAt, string TableName, string PaymentMethod,
    decimal Total, decimal AmountTendered, decimal ChangeGiven, string Cashier);

/// <summary>Everything the Reception dashboard shows: today's takings, pending bills,
/// lifetime totals and a paginated settled-bills history (search included).</summary>
public record ReceptionDashboardDto(
    string Currency,
    decimal TodaySales, int TodayBills, decimal TotalSales, int TotalBills,
    IReadOnlyList<OpenTableDto> Pending,
    IReadOnlyList<SettledBillDto> History, int Page, int PageSize, int HistoryTotal, int TotalPages);

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
    decimal AmountTendered, decimal ChangeGiven, string TaxLabel,
    string FiscalProvider, string FiscalStatus, string? FiscalNumber, string? FiscalQrPayload,
    string? FiscalError, bool ReceiptEnabled = true,
    string LogoUrl = "", string ReceiptHeader = "", string ReceiptFooter = "")
{
    public static InvoiceDto From(Invoice inv, string restaurantName, string currency,
        string logoUrl = "", string receiptHeader = "", string receiptFooter = "",
        bool receiptEnabled = true) => new(
        inv.Id, inv.FormattedNumber, restaurantName, currency, inv.TableCode, inv.TableName,
        inv.IssuedAt, inv.PaymentMethod,
        inv.Lines.Select(l => new BillLineDto(l.Name, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
        inv.Subtotal, inv.ServiceChargePercent, inv.ServiceChargeAmount,
        inv.TaxPercent, inv.TaxAmount, inv.DiscountAmount, inv.Total,
        inv.AmountTendered, inv.ChangeGiven, inv.TaxLabel,
        inv.FiscalProvider, inv.FiscalStatus.ToString(), inv.FiscalNumber, inv.FiscalQrPayload,
        inv.FiscalError, receiptEnabled, logoUrl, receiptHeader, receiptFooter);
}
