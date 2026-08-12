using RestaurantPos.Domain;

namespace RestaurantPos.Application;

public sealed record OrderTotals(decimal Subtotal, decimal Discount, decimal Tax, decimal Total);

public interface IOrderCalculator
{
    OrderTotals Calculate(Order order);
}

public sealed class OrderCalculator : IOrderCalculator
{
    public OrderTotals Calculate(Order order)
    {
        var subtotal = Round(order.Lines.Sum(line => line.UnitPrice * line.Quantity));
        var discount = order.DiscountType switch
        {
            DiscountType.Percentage => Round(subtotal * order.DiscountValue / 100m),
            DiscountType.FixedAmount => Math.Min(subtotal, order.DiscountValue),
            _ => 0m
        };
        decimal tax = 0, allocatedDiscount = 0;
        for (var index = 0; index < order.Lines.Count; index++)
        {
            var line = order.Lines.ElementAt(index);
            var baseAmount = Round(line.UnitPrice * line.Quantity);
            var lineDiscount = index == order.Lines.Count - 1 ? discount - allocatedDiscount : Round(discount * baseAmount / subtotal);
            allocatedDiscount += lineDiscount;
            line.DiscountType = DiscountType.None;
            line.DiscountValue = 0;
            var taxable = baseAmount - lineDiscount;
            line.GstRate = order.GstRate;
            line.TaxAmount = Round(taxable * order.GstRate / 100m);
            line.LineTotal = taxable;
            tax += line.TaxAmount;
        }
        return new OrderTotals(subtotal, Round(discount), Round(tax), Round(subtotal - discount + tax));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
