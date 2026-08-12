using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

public sealed class WpfReceiptPrinter : IReceiptPrinter
{
    public Task<bool> PrintAsync(Order order, bool isReprint, CancellationToken cancellationToken = default)
    {
        var printed = false;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            var document = new FlowDocument { PagePadding = new Thickness(10), FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 10, PageWidth = dialog.PrintableAreaWidth, PageHeight = dialog.PrintableAreaHeight };
            document.Blocks.Add(new Paragraph(new Run("RESTAURANT POS")) { Margin = new Thickness(0), TextAlignment = TextAlignment.Center, FontSize = 15, FontWeight = FontWeights.Bold });
            document.Blocks.Add(new Paragraph(new Run(isReprint ? "INVOICE REPRINT" : "TAX INVOICE")) { Margin = new Thickness(0, 2, 0, 6), TextAlignment = TextAlignment.Center });
            document.Blocks.Add(new Paragraph(new Run($"Invoice: {order.InvoiceNumber}\nDate: {(order.ClosedUtc ?? order.OpenedUtc).ToLocalTime():dd MMM yyyy HH:mm}\nServer: {order.ServerName}\nType: {order.Type}")) { Margin = new Thickness(0, 0, 0, 6) });
            foreach (var line in order.Lines) document.Blocks.Add(new Paragraph(new Run($"{line.ItemName}\n{line.Quantity:N0} x {line.UnitPrice:N2} = {line.LineTotal:N2}")) { Margin = new Thickness(0, 1, 0, 1) });
            document.Blocks.Add(new Paragraph(new Run($"Bill discount: {order.DiscountAmount:N2}\nGST ({order.GstRate:N2}%): {order.TaxAmount:N2}\nTOTAL: {order.GrandTotal:N2}")) { Margin = new Thickness(0, 6, 0, 0), FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0) });
            if (order.Payments.Count > 0) document.Blocks.Add(new Paragraph(new Run($"Payment: {string.Join(", ", order.Payments.Select(x => $"{x.Method} {x.Amount:N2}"))}")) { Margin = new Thickness(0, 6, 0, 0) });
            document.Blocks.Add(new Paragraph(new Run("Thank you for dining with us.")) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"Invoice {order.InvoiceNumber}");
            printed = true;
        });
        return Task.FromResult(printed);
    }
}
