using QrDine.Domain.Billing;
using Xunit;

namespace QrDine.Tests;

/// <summary>Per-item tax + discount distribution math (the FBR-correct model).</summary>
public class InvoiceTaxTests
{
    private static InvoiceLine Line(string name, decimal unit, int qty, decimal taxRate, decimal furtherRate = 0) =>
        new() { ItemCode = name, Name = name, UnitPrice = unit, Qty = qty, TaxRate = taxRate, FurtherTaxRate = furtherRate };

    [Fact]
    public void Price_computes_per_item_tax_and_totals()
    {
        var lines = new[]
        {
            Line("A", 1000m, 2, 5m),    // sale 2000, tax 100
            Line("B", 500m, 1, 16m),    // sale 500,  tax 80
        };

        var m = Invoice.Price(lines, serviceChargePercent: 0, discount: 0);

        Assert.Equal(2500m, m.Subtotal);
        Assert.Equal(180m, m.TaxAmount);
        Assert.Equal(2680m, m.Total);
        Assert.Equal(100m, lines[0].TaxAmount);
        Assert.Equal(80m, lines[1].TaxAmount);
    }

    [Fact]
    public void Discount_is_distributed_by_sale_value_and_tax_is_charged_on_the_net()
    {
        var lines = new[]
        {
            Line("A", 1000m, 2, 5m),    // sale 2000
            Line("B", 500m, 1, 16m),    // sale 500
        };

        var m = Invoice.Price(lines, serviceChargePercent: 0, discount: 250m);

        // Discount split 200 / 50 by sale value; GST is charged on the net (post-discount)
        // line value (the FBR-correct model for a trade discount).
        Assert.Equal(200m, lines[0].DiscountAmount);
        Assert.Equal(50m, lines[1].DiscountAmount);
        Assert.Equal(90m, lines[0].TaxAmount);         // (2000 − 200) × 5%
        Assert.Equal(72m, lines[1].TaxAmount);         // (500 − 50) × 16%
        Assert.Equal(162m, m.TaxAmount);               // 90 + 72 — reduced by the discount
        Assert.Equal(250m, m.DiscountAmount);
        Assert.Equal(2412m, m.Total);                  // 2500 − 250 + 162
        // Line totals reconcile with the invoice total.
        Assert.Equal(m.Total, lines.Sum(l => l.TotalAmount));
    }

    [Fact]
    public void Service_charge_is_added_untaxed()
    {
        var lines = new[] { Line("A", 1000m, 1, 10m) };   // sale 1000, tax 100
        var m = Invoice.Price(lines, serviceChargePercent: 5m, discount: 0);
        Assert.Equal(50m, m.ServiceChargeAmount);          // 5% of 1000
        Assert.Equal(100m, m.TaxAmount);                   // service not taxed
        Assert.Equal(1150m, m.Total);                      // 1000 + 100 + 50
    }

    [Fact]
    public void Further_tax_is_applied_on_sale_value()
    {
        var lines = new[] { Line("A", 1000m, 1, 10m, furtherRate: 1m) };
        var m = Invoice.Price(lines, serviceChargePercent: 0, discount: 0);
        Assert.Equal(10m, m.FurtherTaxAmount);             // 1% of 1000
        Assert.Equal(1110m, m.Total);                      // 1000 + 100 + 10
    }

    [Fact]
    public void Create_snapshots_usin_and_blended_tax_percent()
    {
        var lines = new[] { Line("A", 1000m, 1, 5m), Line("B", 1000m, 1, 15m) };
        var inv = Invoice.Create(1, 1, "T1", "Table 1", lines, 0, 0, "Cash", number: 7, usin: "USIN000007");
        Assert.Equal("USIN000007", inv.Usin);
        Assert.Equal("INV-00007", inv.FormattedNumber);
        Assert.Equal(200m, inv.TaxAmount);                 // 50 + 150
        Assert.Equal(10m, inv.TaxPercent);                 // blended: 200 / 2000 * 100
    }
}
