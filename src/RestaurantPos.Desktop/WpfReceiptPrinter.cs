using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

public sealed class WpfReceiptPrinter : IReceiptPrinter
{
    internal const string RestaurantHeading = "Banaras Dhaba";
    internal static string GetInvoiceHeading(bool isReprint) => isReprint ? "Invoice reprint" : "Invoice";

    public Task<bool> PrintAsync(Order order, bool isReprint, ReceiptPaperWidth paperWidth, CancellationToken cancellationToken = default)
    {
        var printed = false;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            var requestedWidth = GetPaperWidth(paperWidth);
            TrySetPrinterPaperWidth(dialog, requestedWidth);
            var receiptWidth = Math.Min(dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : requestedWidth, requestedWidth);
            var document = new FlowDocument
            {
                PagePadding = new Thickness(6),
                FontFamily = new FontFamily("Arial"),
                FontSize = 9,
                PageWidth = receiptWidth,
                PageHeight = dialog.PrintableAreaHeight,
                ColumnWidth = double.PositiveInfinity
            };

            document.Blocks.Add(Paragraph(RestaurantHeading, TextAlignment.Center, FontWeights.Bold, 12));
            document.Blocks.Add(Paragraph(GetInvoiceHeading(isReprint), TextAlignment.Center, FontWeights.Bold, margin: new Thickness(0, 1, 0, 7)));

            var receiptDate = RestaurantTime.ToLocal(order.ClosedUtc ?? order.OpenedUtc);
            document.Blocks.Add(DetailsTable(
                $"Invoice: {order.InvoiceNumber}", $"Type: {order.Type}",
                $"Date: {receiptDate:dd-MM-yyyy}", $"Time: {receiptDate:HH:mm}"));
            if (!string.IsNullOrWhiteSpace(order.ServerName))
                document.Blocks.Add(Paragraph($"Server: {order.ServerName}", margin: new Thickness(0, 0, 0, 5)));

            var items = new Table { CellSpacing = 0, Margin = new Thickness(0) };
            items.Columns.Add(new TableColumn { Width = new GridLength(50, GridUnitType.Star) });
            items.Columns.Add(new TableColumn { Width = new GridLength(14, GridUnitType.Star) });
            items.Columns.Add(new TableColumn { Width = new GridLength(36, GridUnitType.Star) });
            var rows = new TableRowGroup();
            rows.Rows.Add(ItemRow("Item", "Qty", "Amount", true));
            foreach (var group in GetReceiptGroups(order))
            {
                rows.Rows.Add(ItemRow(group.Heading, "", "", true));
                foreach (var line in group.Lines) rows.Rows.Add(ItemRow(line.ItemName, FormatQuantity(line.Quantity), $"{line.LineTotal:N2}"));
            }
            items.RowGroups.Add(rows);
            document.Blocks.Add(items);

            var subtotal = decimal.Round(order.Lines.Sum(x => x.UnitPrice * x.Quantity), 2, MidpointRounding.AwayFromZero);
            document.Blocks.Add(Paragraph($"Subtotal: {subtotal:N2}", TextAlignment.Right, margin: new Thickness(0, 5, 0, 0)));
            if (order.DiscountAmount > 0)
                document.Blocks.Add(Paragraph($"Bill Discount: {order.DiscountAmount:N2}", TextAlignment.Right));
            document.Blocks.Add(Paragraph($"GST/Tax ({order.GstRate:N2}%): {order.TaxAmount:N2}", TextAlignment.Right));
            document.Blocks.Add(Paragraph($"Total Amount: {order.GrandTotal:N2}", TextAlignment.Center, FontWeights.Bold, 11, new Thickness(0, 7, 0, 0)));

            if (order.Payments.Count > 0)
                document.Blocks.Add(Paragraph($"Payment: {string.Join(", ", order.Payments.Select(x => $"{x.Method} {x.Amount:N2}"))}", TextAlignment.Center, margin: new Thickness(0, 3, 0, 0)));

            document.Blocks.Add(Paragraph("Thank you for dining with us", TextAlignment.Center, FontWeights.Bold, margin: new Thickness(0, 5, 0, 0)));
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"Invoice {order.InvoiceNumber}");
            printed = true;
        });
        return Task.FromResult(printed);
    }

    public Task ExportPdfAsync(Order order, bool isReprint, string filePath, ReceiptPaperWidth paperWidth = ReceiptPaperWidth.Mm80, CancellationToken cancellationToken = default) =>
        CompactPdfReceiptExporter.WriteAsync(order, isReprint, filePath, paperWidth, cancellationToken);

    internal static double GetPaperWidth(ReceiptPaperWidth paperWidth) => (int)paperWidth / 25.4d * 96d;

    private static void TrySetPrinterPaperWidth(PrintDialog dialog, double width)
    {
        try
        {
            var ticket = dialog.PrintTicket;
            var height = ticket.PageMediaSize?.Height ?? dialog.PrintableAreaHeight;
            if (height > 0) ticket.PageMediaSize = new PageMediaSize(width, height);
            ticket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket = ticket;
        }
        catch (PrintSystemException)
        {
            // Some thermal drivers reject custom media. The FlowDocument still constrains content to the selected roll width.
        }
    }

    private static Paragraph Paragraph(string text, TextAlignment alignment = TextAlignment.Left, FontWeight? weight = null,
        double fontSize = 9, Thickness? margin = null) => new(new Run(text))
        {
            Margin = margin ?? new Thickness(0, 1, 0, 1),
            TextAlignment = alignment,
            FontWeight = weight ?? FontWeights.Normal,
            FontSize = fontSize
        };

    private static TableRow ItemRow(string item, string quantity, string amount, bool header = false)
    {
        var row = new TableRow { FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
        row.Cells.Add(Cell(item));
        row.Cells.Add(Cell(quantity, TextAlignment.Center));
        row.Cells.Add(Cell(amount, TextAlignment.Right));
        return row;
    }

    private static Table DetailsTable(string firstLeft, string firstRight, string secondLeft, string secondRight)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        table.Columns.Add(new TableColumn { Width = new GridLength(62, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(38, GridUnitType.Star) });
        var rows = new TableRowGroup();
        rows.Rows.Add(DetailRow(firstLeft, firstRight));
        rows.Rows.Add(DetailRow(secondLeft, secondRight));
        table.RowGroups.Add(rows);
        return table;
    }

    private static TableRow DetailRow(string left, string right)
    {
        var row = new TableRow();
        row.Cells.Add(Cell(left));
        row.Cells.Add(Cell(right));
        return row;
    }

    private static TableCell Cell(string text, TextAlignment alignment = TextAlignment.Left) => new(new Paragraph(new Run(text))
    {
        Margin = new Thickness(0, 2, 0, 2),
        TextAlignment = alignment
    });

    private static string FormatQuantity(decimal quantity) => quantity % 1m == 0 ? quantity.ToString("N0") : quantity.ToString("0.##");
    internal static IReadOnlyList<ReceiptItemGroup> GetReceiptGroups(Order order) => order.Lines.GroupBy(x => x.PreparationMode).OrderBy(x => x.Key).Select(x => new ReceiptItemGroup(x.Key == PreparationMode.Packed ? "TAKEAWAY / PACK" : "DINE IN", x.ToList())).ToList();
    internal sealed record ReceiptItemGroup(string Heading, IReadOnlyList<OrderLine> Lines);
}
