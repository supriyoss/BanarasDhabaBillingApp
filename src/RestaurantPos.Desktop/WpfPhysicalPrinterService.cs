using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RestaurantPos.Application;

namespace RestaurantPos.Desktop;

public sealed class WpfPhysicalPrinterService : IPhysicalPrinterService
{
    public IReadOnlyList<string> GetInstalledPrinterNames()
    {
        using var server = new LocalPrintServer();
        return server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
            .Select(queue => queue.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Task PrintTestPageAsync(string printerName, ReceiptPaperWidth paperWidth, string destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(printerName)) throw new InvalidOperationException($"Select a {destination.ToLowerInvariant()} printer first.");
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            WpfPrintSupport.Print(printerName, dialog =>
            {
                var width = WpfReceiptPrinter.GetPaperWidth(paperWidth);
                WpfPrintSupport.TrySetPaperWidth(dialog, width);
                var document = new FlowDocument
                {
                    PagePadding = new Thickness(8),
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 10,
                    PageWidth = Math.Min(dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : width, width),
                    PageHeight = WpfPrintSupport.GetPrintableHeight(dialog),
                    ColumnWidth = double.PositiveInfinity
                };
                document.Blocks.Add(new Paragraph(new Run("Banaras Dhaba POS")) { FontSize = 13, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
                document.Blocks.Add(new Paragraph(new Run($"{destination} printer test")) { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
                document.Blocks.Add(new Paragraph(new Run($"Printer: {printerName}")));
                document.Blocks.Add(new Paragraph(new Run($"Paper: {(int)paperWidth} mm")));
                document.Blocks.Add(new Paragraph(new Run($"Printed: {DateTime.Now:dd-MM-yyyy HH:mm}")));
                document.Blocks.Add(new Paragraph(new Run("If this text is clear and fully visible, the printer is ready.")) { Margin = new Thickness(0, 8, 0, 0) });
                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"{destination} printer test");
            });
        });
        return Task.CompletedTask;
    }
}

internal static class WpfPrintSupport
{
    internal static bool Print(string? printerName, Action<PrintDialog> print)
    {
        var dialog = new PrintDialog();
        if (string.IsNullOrWhiteSpace(printerName))
        {
            if (dialog.ShowDialog() != true) return false;
            print(dialog);
            return true;
        }

        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
            .FirstOrDefault(candidate => string.Equals(candidate.FullName, printerName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, printerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The configured printer '{printerName}' is not installed or available.");
        queue.Refresh();
        if (queue.IsOffline || queue.IsInError) throw new InvalidOperationException($"The configured printer '{printerName}' is currently unavailable.");
        dialog.PrintQueue = queue;
        print(dialog);
        return true;
    }

    internal static void TrySetPaperWidth(PrintDialog dialog, double width)
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

    internal static double GetPrintableHeight(PrintDialog dialog)
    {
        if (dialog.PrintableAreaHeight > 0) return dialog.PrintableAreaHeight;
        var mediaHeight = dialog.PrintTicket.PageMediaSize?.Height;
        return mediaHeight is > 0 ? mediaHeight.Value : 1122d;
    }
}
