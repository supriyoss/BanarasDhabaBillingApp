using RestaurantPos.Application;
using RestaurantPos.Desktop;
using RestaurantPos.Domain;
using System.Windows.Documents;
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

    [Theory]
    [InlineData(ReceiptPaperWidth.Mm58, 219.21)]
    [InlineData(ReceiptPaperWidth.Mm80, 302.36)]
    public void PhysicalReceipt_UsesSelectedThermalPaperWidth(ReceiptPaperWidth paperWidth, double expectedDip)
    {
        Assert.Equal(expectedDip, WpfReceiptPrinter.GetPaperWidth(paperWidth), 2);
    }

    [Fact]
    public void CompactPdf_HeightTracksActualReceiptContent()
    {
        var shortOrder = ReceiptOrder(1);
        var longOrder = ReceiptOrder(20);

        var shortHeight = CompactPdfReceiptExporter.CalculatePageHeight(shortOrder, false, ReceiptPaperWidth.Mm80);
        var longHeight = CompactPdfReceiptExporter.CalculatePageHeight(longOrder, false, ReceiptPaperWidth.Mm80);
        var pdf = CompactPdfReceiptExporter.BuildPdf(shortOrder, false, ReceiptPaperWidth.Mm80);

        Assert.True(longHeight > shortHeight);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.Latin1.GetString(pdf));
    }

    [Fact]
    public void KotDocument_IsPriceFreeAndContainsKitchenContext()
    {
        var order = ReceiptOrder(1);
        order.ServerName = "Amit";
        var ticket = new KitchenOrderTicket
        {
            TicketNumber = "KOT-TEST-01",
            Order = order,
            Lines = new List<KitchenOrderTicketLine> { new() { ItemName = "Menu item 1", Quantity = 2, PreparationMode = PreparationMode.DineIn } }
        };

        var document = WpfKitchenOrderTicketPrinter.BuildDocument(ticket, 302, 800);
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;

        Assert.Contains("KITCHEN ORDER TICKET", text);
        Assert.Contains("KOT-TEST-01", text);
        Assert.Contains("Menu item 1", text);
        Assert.Contains("Amit", text);
        Assert.DoesNotContain("Amount", text);
        Assert.DoesNotContain("210.00", text);
    }

    [Fact]
    public void FloorEditor_SnapsMovementAndFindsAnOpenCell()
    {
        Assert.Equal(2, InteractiveFloorPlanEditorView.SnapPosition(181, InteractiveFloorPlanEditorView.CellWidth, 11));
        var position = InteractiveFloorPlanEditorView.FindNextPosition([new DiningTable { GridX = 0, GridY = 0, GridWidth = 2, GridHeight = 1 }]);
        Assert.Equal((2, 0), position);
    }

    [Fact]
    public void RestaurantTime_ConvertsUtcOnceToAsiaKolkata()
    {
        var utc = new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 8, 13, 5, 30, 0), RestaurantTime.ToLocal(utc));
        Assert.Equal(new DateTime(2026, 8, 13, 5, 30, 0), RestaurantTime.ToLocal(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
    }

    private static Order ReceiptOrder(int lineCount) => new()
    {
        InvoiceNumber = "POS-TEST",
        Type = OrderType.DineIn,
        OpenedUtc = DateTime.UtcNow,
        GstRate = 5,
        TaxAmount = 10,
        GrandTotal = 210,
        Lines = Enumerable.Range(1, lineCount).Select(index => new OrderLine { ItemName = $"Menu item {index}", Quantity = 1, UnitPrice = 10, LineTotal = 10 }).ToList()
    };
}
