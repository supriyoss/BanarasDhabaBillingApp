using RestaurantPos.Application;
using RestaurantPos.Domain;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class OrderCalculatorTests
{
    [Fact]
    public void BillDiscount_DoesNotChangeCapturedPricesOrLineTotals()
    {
        var order = SampleOrder();
        var totals = new OrderCalculator().Calculate(order);

        Assert.Equal(500m, totals.Subtotal);
        Assert.Equal(50m, totals.Discount);
        Assert.Equal(0m, totals.Tax);
        Assert.Equal(450m, totals.Total);
        Assert.Collection(order.Lines,
            line => { Assert.Equal(240m, line.UnitPrice); Assert.Equal(240m, line.LineTotal); },
            line => { Assert.Equal(220m, line.UnitPrice); Assert.Equal(220m, line.LineTotal); },
            line => { Assert.Equal(40m, line.UnitPrice); Assert.Equal(40m, line.LineTotal); });
    }

    [Fact]
    public void BillDiscount_CalculatesTaxOnDiscountedBillUsingExistingTaxRule()
    {
        var order = SampleOrder();
        order.GstRate = 5m;
        var totals = new OrderCalculator().Calculate(order);

        Assert.Equal(22.50m, totals.Tax);
        Assert.Equal(472.50m, totals.Total);
        Assert.Equal(500m, order.Lines.Sum(x => x.LineTotal));
        Assert.Equal(22.50m, order.Lines.Sum(x => x.TaxAmount));
    }

    [Fact]
    public void Calculation_DoesNotOverwriteDistinctItemDiscountFields()
    {
        var order = SampleOrder();
        var line = order.Lines.First();
        line.DiscountType = DiscountType.Percentage;
        line.DiscountValue = 10m;
        new OrderCalculator().Calculate(order);

        Assert.Equal(DiscountType.Percentage, line.DiscountType);
        Assert.Equal(10m, line.DiscountValue);
    }

    private static Order SampleOrder() => new()
    {
        DiscountType = DiscountType.FixedAmount,
        DiscountValue = 50m,
        Lines = new List<OrderLine>
        {
            new() { ItemName = "Paneer Tikka", Quantity = 1, UnitPrice = 240m },
            new() { ItemName = "Veg Biryani", Quantity = 1, UnitPrice = 220m },
            new() { ItemName = "Masala Chai", Quantity = 1, UnitPrice = 40m }
        }
    };
}
