using RestaurantPos.Desktop;
using RestaurantPos.Domain;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class PatchReleaseTests
{
    [Fact]
    public void Receipt_UsesRestaurantNameAndInvoiceHeading()
    {
        Assert.Equal("Banaras Dhaba", WpfReceiptPrinter.RestaurantHeading);
        Assert.Equal("Invoice", WpfReceiptPrinter.GetInvoiceHeading(false));
        Assert.Equal("Invoice reprint", WpfReceiptPrinter.GetInvoiceHeading(true));
    }

    [Fact]
    public void Receipt_DistinguishesDineInAndPackedItems()
    {
        var order = new Order { Lines = new List<OrderLine> { new() { ItemName = "Meal", PreparationMode = PreparationMode.DineIn }, new() { ItemName = "Tea", PreparationMode = PreparationMode.Packed } } };
        Assert.Equal(new[] { "DINE IN", "TAKEAWAY / PACK" }, WpfReceiptPrinter.GetReceiptGroups(order).Select(x => x.Heading));
    }

    [Fact]
    public void RestaurantTime_ConvertsUtcOnceToAsiaKolkata()
    {
        var utc = new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 8, 13, 5, 30, 0), RestaurantTime.ToLocal(utc));
        Assert.Equal(new DateTime(2026, 8, 13, 5, 30, 0), RestaurantTime.ToLocal(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
    }
}
