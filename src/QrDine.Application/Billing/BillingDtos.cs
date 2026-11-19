using QrDine.Domain.Billing;

namespace QrDine.Application.Billing;

public record BillLineDto(string Name, int Qty, decimal UnitPrice, decimal LineTotal);

/// <summary>A live, unsettled bill preview for a table. Reflects the current
/// tax/discount settings so the cashier screen shows exactly what will be charged.</summary>
public record GiveBackRequest(decimal Amount, string? Reason);
public record GiveBackPart(string Method, decimal Amount);
/// <summary>How the money goes back (by the method each part came in), and what is still paid on the table.</summary>
public record GiveBackResult(IReadOnlyList<GiveBackPart> Parts, decimal StillPaid);

public record BillDto(
    string RestaurantName, string Currency, string TableCode, string TableName,
    IReadOnlyList<BillLineDto> Lines,
    decimal Subtotal, decimal ServiceChargePercent, decimal ServiceChargeAmount,
    decimal TaxPercent, decimal TaxAmount, decimal Total, bool HasOpenOrders,
    decimal AmountPaid, decimal Remaining,
    bool TaxEnabled, string TaxName, bool DiscountEnabled, decimal DiscountAmount,
    bool ReceiptEnabled,
    string LogoUrl = "", string ReceiptHeader = "", string ReceiptFooter = "",
    string Channel = "Dine", string CustomerName = "", string CustomerPhone = "", string DeliveryAddress = "", string RiderName = "",
    // FurtherTaxAmount is in Total and is printed as its own line. SplitRemaining is what is left at the standard
    // rate: parts are always booked at it, so the split screen divides this, not a card-rate Remaining.
    // ServiceTaxAmount (tax on the service charge) and RoundingAmount (to a whole rupee) are in Total too, and are
    // 0 unless the owner turned those bill options on.
    decimal FurtherTaxAmount = 0, decimal SplitRemaining = 0, decimal ServiceTaxAmount = 0, decimal RoundingAmount = 0);

// Discount is the applied Rs amount the cashier confirmed (pre-filled from settings);
// AmountTendered is the cash received (used to compute & store the change).
// ExpectedTotal is the amount still due that the cashier saw; if the bill changed since (a new order came in),
// settling is refused so nobody takes money against an old total.
public record SettleRequest(string Method, decimal Discount, decimal AmountTendered = 0, int? CustomerId = null, decimal? ExpectedTotal = null);
public record PayPartRequest(decimal Amount, string Method, decimal? Discount = null);
public record PayPartResult(bool Settled, decimal Paid, decimal Remaining, InvoiceDto? Invoice);

/// <summary>An open bill's total now, and what is still to pay on it after any parts.</summary>
public record OpenBillTotal(decimal Total, decimal Remaining);

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
    string? FiscalError, int FiscalAttempts, DateTime? FiscalLastAttemptAt, long? SyncedByPosId,
    string Office = "", string Environment = "", bool BeingSent = false, decimal Tax = 0);

/// <summary>What one "send" press did. Left: bills still waiting or refused after it.</summary>
public record FiscalBatchResult(int Sent, int Refused, int StillWaiting, int Skipped, int Left, string? Stopped);

/// <summary>A branch's filing setup: Ready when nothing stops its bills going to the tax office.</summary>
public record FiscalBranchCheck(int BranchId, string BranchName, string Office, string Environment, string Mode,
    bool Ready, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings, bool ForcedPractice = false);

/// <summary>A test bill sent to a branch's Test (or Practice) setup.</summary>
public record FiscalTestResult(bool Accepted, string Office, string Environment, string? Number, string? Error,
    decimal Total, decimal Tax);

/// <summary>Ids: the bills picked on the Pending list; none means every waiting bill.</summary>
public record FiscalRetryManyRequest(IReadOnlyList<int>? Ids);

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
    string LogoUrl = "", string ReceiptHeader = "", string ReceiptFooter = "",
    string Channel = "Dine", string CustomerName = "", string CustomerPhone = "", string DeliveryAddress = "", string RiderName = "",
    decimal FurtherTaxAmount = 0, decimal ServiceTaxAmount = 0, decimal RoundingAmount = 0,
    string FiscalEnvironment = "", string SellerNtn = "", string SellerStrn = "", long? PosId = null)
{
    /// <summary>Which tax office's name goes on the receipt (a practice number from older builds said "Simulated").</summary>
    public string FiscalOffice => FiscalProvider is "PRA" ? "PRA" : "FBR";

    public static InvoiceDto From(Invoice inv, string restaurantName, string currency,
        string logoUrl = "", string receiptHeader = "", string receiptFooter = "",
        bool receiptEnabled = true, string sellerNtn = "", string sellerStrn = "") => new(
        inv.Id, inv.FormattedNumber, restaurantName, currency, inv.TableCode, inv.TableName,
        inv.IssuedAt, inv.PaymentMethod,
        inv.Lines.Select(l => new BillLineDto(l.Name, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
        inv.Subtotal, inv.ServiceChargePercent, inv.ServiceChargeAmount,
        inv.TaxPercent, inv.TaxAmount, inv.DiscountAmount, inv.Total,
        inv.AmountTendered, inv.ChangeGiven, inv.TaxLabel,
        inv.FiscalProvider, inv.FiscalStatus.ToString(), inv.FiscalNumber, inv.FiscalQrPayload,
        inv.FiscalError, receiptEnabled, logoUrl, receiptHeader, receiptFooter,
        inv.Channel.ToString(), inv.BuyerName, inv.BuyerPhone, inv.DeliveryAddress, inv.RiderName, inv.FurtherTaxAmount,
        inv.ServiceTaxAmount, inv.RoundingAmount,
        inv.FiscalEnvironment is "" && inv.FiscalProvider is "Simulated" ? "Practice" : inv.FiscalEnvironment,
        sellerNtn, sellerStrn, inv.SyncedByPosId);
}
