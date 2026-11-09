using QrDine.Domain.Common;

namespace QrDine.Domain.Billing;

/// <summary>
/// A settled bill for one table session. Amounts and per-item tax are snapshotted
/// at settle time so a later menu or tax change never alters a historical invoice.
/// The Number is sequential per restaurant; USIN is the unique sale id the FBR
/// fiscal integration keys on (so the same sale is never filed twice).
/// </summary>
public class Invoice : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public int TableSessionId { get; set; }
    public int Number { get; set; }                 // sequential per restaurant
    public string Usin { get; set; } = "";          // unique sale id for FBR (idempotency key)
    public string TableCode { get; set; } = "";
    public string TableName { get; set; } = "";
    public Ordering.OrderChannel Channel { get; set; } = Ordering.OrderChannel.Dine;
    public string DeliveryAddress { get; set; } = "";
    public string RiderName { get; set; } = "";

    public decimal Subtotal { get; set; }           // sum of line sale values (tax-exclusive)
    public decimal TaxPercent { get; set; }         // blended display rate = Tax / Subtotal
    public decimal TaxAmount { get; set; }
    public decimal FurtherTaxAmount { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceChargeAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }

    // Cash handling — snapshotted so a historical bill always shows what was tendered
    // and the change given back, regardless of later settings changes.
    public decimal AmountTendered { get; set; }        // cash/amount received from the customer
    public decimal ChangeGiven { get; set; }           // change returned = max(0, tendered - total)
    public string TaxLabel { get; set; } = "";         // e.g. "GST 15%" — snapshot of the tax name+rate

    public string PaymentMethod { get; set; } = "Cash";
    public int InvoiceType { get; set; } = 1;       // FBR: 1 = New
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByUsername { get; set; } = "";   // the cashier who settled it

    // Optional buyer details (printed / filed if captured).
    public string BuyerName { get; set; } = "";
    public string BuyerNtn { get; set; } = "";
    public string BuyerCnic { get; set; } = "";
    public string BuyerPhone { get; set; } = "";

    // Fiscal (FBR) registration. Filled in at settle time / on manual retry.
    // FiscalNumber is the authority's invoice id printed on the receipt; the QR
    // encodes FiscalQrPayload. Error/attempt fields drive the pending-invoice view.
    public string FiscalProvider { get; set; } = "None";
    public FiscalStatus FiscalStatus { get; set; } = FiscalStatus.NotRequired;
    public string? FiscalNumber { get; set; }
    public string? FiscalQrPayload { get; set; }
    public string? FiscalError { get; set; }
    public int FiscalAttempts { get; set; }
    public DateTime? FiscalLastAttemptAt { get; set; }
    public long? SyncedByPosId { get; set; }        // which POS registration filed it

    public List<InvoiceLine> Lines { get; set; } = new();

    public string FormattedNumber => $"INV-{Number:D5}";

    /// <summary>True once accepted by FBR — the guard that prevents a duplicate filing.</summary>
    public bool IsFiscalised => FiscalStatus == FiscalStatus.Registered && !string.IsNullOrEmpty(FiscalNumber);

    /// <summary>A successful FBR registration: record the number/QR and clear any error.</summary>
    public void ApplyFiscalSuccess(string provider, string? number, string? qrPayload, long? posId)
    {
        FiscalProvider = provider;
        FiscalStatus = FiscalStatus.Registered;
        FiscalNumber = number;
        FiscalQrPayload = qrPayload;
        FiscalError = null;
        FiscalAttempts++;
        FiscalLastAttemptAt = DateTime.UtcNow;
        SyncedByPosId = posId;
    }

    /// <summary>A failed/queued attempt: keep the sale, record the error for retry.</summary>
    public void ApplyFiscalFailure(string provider, FiscalStatus status, string error, long? posId)
    {
        FiscalProvider = provider;
        FiscalStatus = status;   // Pending (queued/offline) or Failed (rejected)
        FiscalError = error;
        FiscalAttempts++;
        FiscalLastAttemptAt = DateTime.UtcNow;
        if (posId is not null) SyncedByPosId = posId;
    }

    /// <summary>Fiscal is off for this restaurant — nothing to file.</summary>
    public void MarkFiscalNotRequired()
    {
        FiscalProvider = "None";
        FiscalStatus = FiscalStatus.NotRequired;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>The computed money totals for a set of lines. The single source of
    /// truth for both the live bill preview and the settled invoice.</summary>
    public readonly record struct BillMath(decimal Subtotal, decimal TaxAmount,
        decimal FurtherTaxAmount, decimal ServiceChargeAmount, decimal DiscountAmount, decimal Total);

    /// <summary>
    /// Price a set of lines that already carry their resolved TaxRate / FurtherTaxRate.
    /// Mutates each line's SaleValue / DiscountAmount / TaxAmount / FurtherTaxAmount /
    /// TotalAmount and returns the invoice totals. Calculation order (per spec):
    /// Items → Subtotal → Discount → Tax/GST → Grand Total. Tax is charged on the
    /// discounted (net) line value, so a discount reduces both the base and the tax.
    /// The service charge is added at invoice level on the net subtotal and is untaxed.
    /// </summary>
    public static BillMath Price(IReadOnlyList<InvoiceLine> lines, decimal serviceChargePercent, decimal discount)
    {
        foreach (var l in lines) l.SaleValue = Round(l.UnitPrice * l.Qty);

        var subtotal = Round(lines.Sum(l => l.SaleValue));
        if (discount < 0) discount = 0;
        if (discount > subtotal) discount = subtotal;

        decimal allocated = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            // Distribute the bill discount across lines proportionally by sale value;
            // the last line absorbs the rounding remainder so the parts sum exactly.
            if (subtotal <= 0) l.DiscountAmount = 0;
            else if (i == lines.Count - 1) l.DiscountAmount = Round(discount - allocated);
            else { l.DiscountAmount = Round(discount * (l.SaleValue / subtotal)); allocated += l.DiscountAmount; }

            // Tax is charged on the net (post-discount) value of the line.
            var net = l.SaleValue - l.DiscountAmount;
            l.TaxAmount = Round(net * l.TaxRate / 100m);
            l.FurtherTaxAmount = Round(net * l.FurtherTaxRate / 100m);
            l.TotalAmount = Round(net + l.TaxAmount + l.FurtherTaxAmount);
        }

        var tax = Round(lines.Sum(l => l.TaxAmount));
        var furtherTax = Round(lines.Sum(l => l.FurtherTaxAmount));
        var service = Round(subtotal * serviceChargePercent / 100m);
        var total = Round(subtotal + service + tax + furtherTax - discount);
        return new BillMath(subtotal, tax, furtherTax, service, Round(discount), total);
    }

    /// <summary>Build a settled invoice from priced lines (see <see cref="Price"/>).</summary>
    public static Invoice Create(int restaurantId, int sessionId, string tableCode, string tableName,
        IReadOnlyList<InvoiceLine> lines, decimal serviceChargePercent, decimal discount,
        string method, int number, string usin,
        decimal amountTendered = 0, string taxLabel = "")
    {
        var m = Price(lines, serviceChargePercent, discount);
        // Change is only meaningful for cash: never negative, and 0 when the exact
        // amount (or no explicit tender) was recorded.
        var change = amountTendered > m.Total ? Round(amountTendered - m.Total) : 0;
        return new Invoice
        {
            RestaurantId = restaurantId,
            TableSessionId = sessionId,
            Number = number,
            Usin = usin,
            TableCode = tableCode,
            TableName = tableName,
            Lines = lines.ToList(),
            Subtotal = m.Subtotal,
            ServiceChargePercent = serviceChargePercent,
            ServiceChargeAmount = m.ServiceChargeAmount,
            // Display rate is tax over the net (post-discount) base it was charged on,
            // so the receipt shows the real GST rate (e.g. 10%), not a discount-blended one.
            TaxPercent = (m.Subtotal - m.DiscountAmount) > 0 ? Round(m.TaxAmount / (m.Subtotal - m.DiscountAmount) * 100m) : 0,
            TaxAmount = m.TaxAmount,
            FurtherTaxAmount = m.FurtherTaxAmount,
            DiscountAmount = m.DiscountAmount,
            Total = m.Total,
            AmountTendered = amountTendered > 0 ? Round(amountTendered) : Round(m.Total),
            ChangeGiven = change,
            TaxLabel = taxLabel,
            PaymentMethod = string.IsNullOrWhiteSpace(method) ? "Cash" : method,
            InvoiceType = 1,
            IssuedAt = DateTime.UtcNow,
        };
    }
}

/// <summary>A partial payment recorded toward an open table's bill (split-bill).</summary>
public class Payment : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public int TableSessionId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Where an invoice stands with the tax authority.</summary>
public enum FiscalStatus
{
    NotRequired,  // fiscal integration is off for this restaurant
    Registered,   // accepted by the authority, has a fiscal number
    Pending,      // sale completed but not yet posted (offline/queued) — retry later
    Failed,       // authority rejected it — see FiscalError, retry later
}

/// <summary>
/// One settled line, snapshotting the item's tax at settle time. SaleValue is the
/// tax-exclusive amount FBR calculates tax on; TotalAmount folds in tax and this
/// line's share of the bill discount.
/// </summary>
public class InvoiceLine : Entity
{
    /// <summary>The item code on the line that carries a delivery charge rather than a dish.</summary>
    public const string DeliveryCode = "DELIVERY";

    public int InvoiceId { get; set; }
    public string ItemCode { get; set; } = "";      // menu item id (FBR ItemCode)
    public string Name { get; set; } = "";
    public string PctCode { get; set; } = "";       // FBR Pakistan Customs Tariff / HS code
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }          // tax-exclusive unit price

    public decimal TaxRate { get; set; }            // resolved % (item or restaurant default)
    public decimal FurtherTaxRate { get; set; }

    // Computed & snapshotted by Invoice.Create.
    public decimal SaleValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal FurtherTaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public decimal LineTotal => UnitPrice * Qty;    // tax-exclusive line subtotal (display)
}
