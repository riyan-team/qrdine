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
    public decimal ServiceTaxPercent { get; set; }  // the rate it was charged at (0 = service untaxed)
    public decimal ServiceTaxAmount { get; set; }   // tax on the service charge, when the owner charges it
    public decimal DiscountAmount { get; set; }
    public decimal RoundingAmount { get; set; }     // to the nearest whole rupee, when the owner rounds bills (can be negative)
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
        decimal FurtherTaxAmount, decimal ServiceChargeAmount, decimal DiscountAmount, decimal Total,
        decimal ServiceTaxAmount = 0, decimal RoundingAmount = 0);

    /// <summary>
    /// How the bill-level charges work, as the owner set them. The defaults are how bills always worked:
    /// service on the subtotal before discount, no tax on it, and totals kept to the paisa.
    /// </summary>
    /// <param name="ServicePercent">Service charge % (0 for parcels and deliveries).</param>
    /// <param name="ServiceAfterDiscount">Work the service charge out on the food after the discount.</param>
    /// <param name="ServiceTaxRate">Tax % charged on the service charge (0 = none).</param>
    /// <param name="RoundToRupee">Round the grand total to the nearest whole rupee.</param>
    public readonly record struct Charges(decimal ServicePercent, bool ServiceAfterDiscount = false,
        decimal ServiceTaxRate = 0, bool RoundToRupee = false);

    public static BillMath Price(IReadOnlyList<InvoiceLine> lines, decimal serviceChargePercent, decimal discount) =>
        Price(lines, new Charges(serviceChargePercent), discount);

    /// <summary>
    /// Price a set of lines that already carry their resolved TaxRate / FurtherTaxRate.
    /// Mutates each line's SaleValue / DiscountAmount / TaxAmount / FurtherTaxAmount /
    /// TotalAmount and returns the invoice totals. Calculation order (per spec):
    /// Items → Subtotal → Discount → Tax/GST → Grand Total. Tax is charged on the
    /// discounted (net) line value, so a discount reduces both the base and the tax.
    /// The discount comes off the food only: the delivery charge is untaxed and is never discounted,
    /// so it can't soak up part of a discount that should have lowered the tax.
    /// The service charge is added at invoice level: by default on the subtotal before discount and untaxed;
    /// the owner can have it worked out after the discount and/or taxed (see <see cref="Charges"/>).
    /// Rounding to a whole rupee, when on, is the very last step and is kept as its own amount.
    /// </summary>
    public static BillMath Price(IReadOnlyList<InvoiceLine> lines, Charges charges, decimal discount)
    {
        foreach (var l in lines) l.SaleValue = Round(l.UnitPrice * l.Qty);

        var subtotal = Round(lines.Sum(l => l.SaleValue));
        var food = lines.Where(l => !l.IsDelivery).ToList();
        var foodSubtotal = Round(food.Sum(l => l.SaleValue));
        if (discount < 0) discount = 0;
        if (discount > foodSubtotal) discount = foodSubtotal;

        // Distribute the bill discount across the food lines proportionally by sale value. The biggest
        // food line absorbs the rounding remainder, so the parts sum exactly and no line goes below zero.
        var biggest = foodSubtotal > 0 ? food.MaxBy(l => l.SaleValue) : null;
        decimal allocated = 0;
        foreach (var l in lines)
        {
            l.DiscountAmount = l.IsDelivery || biggest is null || ReferenceEquals(l, biggest) ? 0 : Round(discount * (l.SaleValue / foodSubtotal));
            allocated += l.DiscountAmount;
        }
        if (biggest is not null) biggest.DiscountAmount = Round(discount - allocated);

        foreach (var l in lines)
        {
            // Tax is charged on the net (post-discount) value of the line.
            var net = l.SaleValue - l.DiscountAmount;
            l.TaxAmount = Round(net * l.TaxRate / 100m);
            l.FurtherTaxAmount = Round(net * l.FurtherTaxRate / 100m);
            l.TotalAmount = Round(net + l.TaxAmount + l.FurtherTaxAmount);
        }

        var tax = Round(lines.Sum(l => l.TaxAmount));
        var furtherTax = Round(lines.Sum(l => l.FurtherTaxAmount));
        var serviceBase = charges.ServiceAfterDiscount ? Math.Max(0, subtotal - discount) : subtotal;
        var service = Round(serviceBase * charges.ServicePercent / 100m);
        var serviceTax = charges.ServiceTaxRate > 0 ? Round(service * charges.ServiceTaxRate / 100m) : 0;
        var exact = Round(subtotal + service + serviceTax + tax + furtherTax - discount);
        var total = charges.RoundToRupee ? Math.Round(exact, 0, MidpointRounding.AwayFromZero) : exact;
        return new BillMath(subtotal, tax, furtherTax, service, Round(discount), total, serviceTax, Round(total - exact));
    }

    /// <summary>
    /// The rate to print next to the tax: the tax over the net value of the lines that were taxed, so the
    /// receipt shows the real GST rate (e.g. 16%) and not one watered down by a delivery charge or a tax-free
    /// item. Lines must be priced first. Mixed rates give their weighted average.
    /// </summary>
    public static decimal DisplayTaxRate(IReadOnlyList<InvoiceLine> lines)
    {
        var taxedBase = lines.Where(l => l.TaxRate > 0).Sum(l => l.SaleValue - l.DiscountAmount);
        return taxedBase > 0 ? Round(lines.Sum(l => l.TaxAmount) / taxedBase * 100m) : 0;
    }

    /// <summary>Build a settled invoice from priced lines (see <see cref="Price"/>).</summary>
    /// <param name="alreadyPaid">Parts of the bill paid before this one. The money handed over now is
    /// for what is left, so the change is worked out against that, not against the whole bill.</param>
    public static Invoice Create(int restaurantId, int sessionId, string tableCode, string tableName,
        IReadOnlyList<InvoiceLine> lines, decimal serviceChargePercent, decimal discount,
        string method, int number, string usin,
        decimal amountTendered = 0, string taxLabel = "", decimal alreadyPaid = 0) =>
        Create(restaurantId, sessionId, tableCode, tableName, lines, new Charges(serviceChargePercent), discount,
            method, number, usin, amountTendered, taxLabel, alreadyPaid);

    public static Invoice Create(int restaurantId, int sessionId, string tableCode, string tableName,
        IReadOnlyList<InvoiceLine> lines, Charges charges, decimal discount,
        string method, int number, string usin,
        decimal amountTendered = 0, string taxLabel = "", decimal alreadyPaid = 0)
    {
        var serviceChargePercent = charges.ServicePercent;
        var m = Price(lines, charges, discount);
        var due = Math.Max(0, Round(m.Total - Math.Max(0, alreadyPaid)));
        // Change is only meaningful for cash: never negative, and 0 when the exact
        // amount (or no explicit tender) was recorded.
        var change = amountTendered > due ? Round(amountTendered - due) : 0;
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
            ServiceTaxPercent = m.ServiceTaxAmount > 0 ? charges.ServiceTaxRate : 0,
            ServiceTaxAmount = m.ServiceTaxAmount,
            RoundingAmount = m.RoundingAmount,
            TaxPercent = DisplayTaxRate(lines),
            TaxAmount = m.TaxAmount,
            FurtherTaxAmount = m.FurtherTaxAmount,
            DiscountAmount = m.DiscountAmount,
            Total = m.Total,
            // Everything received for this bill, earlier parts included, so received minus change is always the total.
            AmountTendered = Round(Math.Max(0, alreadyPaid) + (amountTendered > 0 ? amountTendered : due)),
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

    public bool IsDelivery => ItemCode == DeliveryCode;
}
