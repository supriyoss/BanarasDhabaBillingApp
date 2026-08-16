using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

public sealed class WpfKitchenOrderTicketPrinter : IKitchenOrderTicketPrinter
{
    public Task<bool> PrintAsync(KitchenOrderTicket ticket, ReceiptPaperWidth paperWidth, CancellationToken cancellationToken = default)
    {
        var printed = false;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            var requestedWidth = WpfReceiptPrinter.GetPaperWidth(paperWidth);
            TrySetPrinterPaperWidth(dialog, requestedWidth);
            var document = BuildDocument(ticket, Math.Min(dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : requestedWidth, requestedWidth), dialog.PrintableAreaHeight);
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, ticket.TicketNumber);
            printed = true;
        });
        return Task.FromResult(printed);
    }

    internal static FlowDocument BuildDocument(KitchenOrderTicket ticket, double pageWidth, double pageHeight)
    {
        var order = ticket.Order ?? throw new InvalidOperationException("The KOT is missing its order details.");
        var document = new FlowDocument
        {
            PagePadding = new Thickness(6),
            FontFamily = new FontFamily("Arial"),
            FontSize = 10,
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            ColumnWidth = double.PositiveInfinity
        };
        document.Blocks.Add(Paragraph("Banaras Dhaba", TextAlignment.Center, FontWeights.Bold, 12));
        document.Blocks.Add(Paragraph(ticket.IsSupplementary ? "SUPPLEMENTARY KOT" : "KITCHEN ORDER TICKET", TextAlignment.Center, FontWeights.Bold, 11, new Thickness(0, 1, 0, 6)));
        document.Blocks.Add(Paragraph($"KOT: {ticket.TicketNumber}", weight: FontWeights.Bold));
        document.Blocks.Add(Paragraph($"Order: {order.InvoiceNumber}"));
        document.Blocks.Add(Paragraph($"Time: {ticket.CreatedLocal:dd-MM-yyyy HH:mm}"));
        document.Blocks.Add(Paragraph(order.Type == OrderType.DineIn ? $"DINE-IN: {FormatTable(order)}" : "TAKEAWAY", weight: FontWeights.Bold));
        if (!string.IsNullOrWhiteSpace(order.ServerName)) document.Blocks.Add(Paragraph($"Server: {order.ServerName}"));

        var items = new Table { CellSpacing = 0, Margin = new Thickness(0, 7, 0, 0) };
        items.Columns.Add(new TableColumn { Width = new GridLength(16, GridUnitType.Star) });
        items.Columns.Add(new TableColumn { Width = new GridLength(58, GridUnitType.Star) });
        items.Columns.Add(new TableColumn { Width = new GridLength(26, GridUnitType.Star) });
        var rows = new TableRowGroup();
        rows.Rows.Add(ItemRow("Qty", "Item", "Mode", true));
        foreach (var line in ticket.Lines) rows.Rows.Add(ItemRow(FormatQuantity(line.Quantity), line.ItemName, line.PreparationMode == PreparationMode.Packed ? "PACK" : "DINE-IN"));
        items.RowGroups.Add(rows);
        document.Blocks.Add(items);
        document.Blocks.Add(Paragraph("--- KITCHEN COPY ---", TextAlignment.Center, FontWeights.Bold, 9, new Thickness(0, 8, 0, 0)));
        return document;
    }

    private static string FormatTable(Order order)
    {
        var table = order.DiningTable?.Name ?? "Table not specified";
        return order.DiningTable?.FloorLayout is null ? table : $"{order.DiningTable.FloorLayout.Name} • {table}";
    }

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
            // Some thermal drivers reject custom media. The document still uses the configured roll width.
        }
    }

    private static Paragraph Paragraph(string text, TextAlignment alignment = TextAlignment.Left, FontWeight? weight = null, double fontSize = 10, Thickness? margin = null) => new(new Run(text))
    {
        Margin = margin ?? new Thickness(0, 1, 0, 1),
        TextAlignment = alignment,
        FontWeight = weight ?? FontWeights.Normal,
        FontSize = fontSize
    };

    private static TableRow ItemRow(string quantity, string item, string mode, bool header = false)
    {
        var row = new TableRow { FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
        row.Cells.Add(Cell(quantity, TextAlignment.Center));
        row.Cells.Add(Cell(item));
        row.Cells.Add(Cell(mode, TextAlignment.Right));
        return row;
    }

    private static TableCell Cell(string text, TextAlignment alignment = TextAlignment.Left) => new(new Paragraph(new Run(text))
    {
        Margin = new Thickness(0, 3, 0, 3),
        TextAlignment = alignment
    });

    private static string FormatQuantity(decimal quantity) => quantity % 1m == 0 ? quantity.ToString("N0") : quantity.ToString("0.##");
}
