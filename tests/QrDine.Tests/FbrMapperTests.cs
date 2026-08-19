using QrDine.Application.Billing;
using QrDine.Domain.Billing;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure.Fiscal;
using Xunit;

namespace QrDine.Tests;

/// <summary>The FBR payload mapper: field mapping, total reconciliation, payment mode.</summary>
public class FbrMapperTests
{
    private static InvoiceLine Line(string name, decimal unit, int qty, decimal taxRate, string pct) =>
        new() { ItemCode = name, Name = name, UnitPrice = unit, Qty = qty, TaxRate = taxRate, PctCode = pct };

    private static FiscalContext Ctx(long posId = 110014) =>
        new("Test Biz", "1234567", "12-34", FbrMode.Cloud, true, null, posId, "tok");

    [Fact]
    public void Map_produces_per_item_lines_and_reconciling_totals()
    {
        var lines = new[] { Line("1", 1000m, 2, 5m, "1101"), Line("2", 500m, 1, 16m, "2202") };
        var inv = Invoice.Create(1, 1, "T1", "Table 1", lines, serviceChargePercent: 0, discount: 0,
            method: "Cash", number: 1, usin: "USIN000001");

        var model = FbrInvoiceMapper.Map(inv, Ctx());

        Assert.Equal(110014, model.POSID);
        Assert.Equal("USIN000001", model.USIN);
        Assert.Equal(1, model.PaymentMode);                 // Cash
        Assert.Equal(2, model.Items.Count);
        Assert.Equal("1101", model.Items[0].PCTCode);
        Assert.Equal(5, model.Items[0].TaxRate);
        Assert.Equal(2000, model.Items[0].SaleValue);
        Assert.Equal(100, model.Items[0].TaxCharged);
        // Totals reconcile with the invoice.
        Assert.Equal((double)inv.TaxAmount, model.TotalTaxCharged, 3);
        Assert.Equal((double)inv.Total, model.TotalBillAmount, 3);
        Assert.Equal(model.Items.Sum(i => i.TotalAmount), model.TotalBillAmount, 3);
    }

    [Fact]
    public void Map_adds_service_charge_as_a_zero_tax_line_so_totals_reconcile()
    {
        var lines = new[] { Line("1", 1000m, 1, 10m, "1101") };
        var inv = Invoice.Create(1, 1, "T1", "Table 1", lines, serviceChargePercent: 5m, discount: 0,
            method: "Card", number: 2, usin: "USIN000002");

        var model = FbrInvoiceMapper.Map(inv, Ctx());

        Assert.Equal(2, model.Items.Count);                 // item + service line
        var service = model.Items.Single(i => i.ItemCode == "SERVICE");
        Assert.Equal(0, service.TaxCharged);
        Assert.Equal(50, service.SaleValue);
        Assert.Equal((double)inv.Total, model.TotalBillAmount, 3);
    }

    [Theory]
    [InlineData("Cash", 1)]
    [InlineData("Card", 2)]
    [InlineData("JazzCash", 2)]
    [InlineData("Cheque", 6)]
    [InlineData("Split (Cash+Card)", 5)]
    public void PaymentMode_maps_correctly(string method, int expected)
        => Assert.Equal(expected, FbrInvoiceMapper.PaymentMode(method));
}
