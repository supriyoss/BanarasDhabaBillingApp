using System.Globalization;
using System.IO;
using System.Text;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

internal static class CompactPdfReceiptExporter
{
    private const double PointsPerMillimetre = 72d / 25.4d;
    private const double HorizontalMargin = 9d;
    private const double VerticalMargin = 10d;

    public static Task WriteAsync(Order order, bool isReprint, string filePath, ReceiptPaperWidth paperWidth, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(filePath, BuildPdf(order, isReprint, paperWidth), cancellationToken);

    internal static byte[] BuildPdf(Order order, bool isReprint, ReceiptPaperWidth paperWidth)
    {
        var lines = BuildLines(order, isReprint, paperWidth);
        var width = (int)paperWidth * PointsPerMillimetre;
        var height = CalculatePageHeight(lines);
        var content = BuildContentStream(lines, width, height);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(width)} {Number(height)}] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>"
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xrefOffset = Encoding.Latin1.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append(offset.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xrefOffset).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    internal static double CalculatePageHeight(Order order, bool isReprint, ReceiptPaperWidth paperWidth) => CalculatePageHeight(BuildLines(order, isReprint, paperWidth));
    private static double CalculatePageHeight(IReadOnlyList<ReceiptLine> lines) => Math.Max(72, VerticalMargin * 2 + lines.Sum(x => x.FontSize + 3));

    private static string BuildContentStream(IReadOnlyList<ReceiptLine> lines, double pageWidth, double pageHeight)
    {
        var builder = new StringBuilder();
        var y = pageHeight - VerticalMargin;
        foreach (var line in lines)
        {
            y -= line.FontSize;
            var textWidth = line.Text.Length * line.FontSize * 0.6d;
            var x = line.Alignment switch
            {
                ReceiptAlignment.Center => Math.Max(HorizontalMargin, (pageWidth - textWidth) / 2),
                ReceiptAlignment.Right => Math.Max(HorizontalMargin, pageWidth - HorizontalMargin - textWidth),
                _ => HorizontalMargin
            };
            builder.Append("BT /").Append(line.Bold ? "F2" : "F1").Append(' ').Append(Number(line.FontSize)).Append(" Tf ")
                .Append(Number(x)).Append(' ').Append(Number(y)).Append(" Td (").Append(Escape(line.Text)).Append(") Tj ET\n");
            y -= 3;
        }
        return builder.ToString();
    }

    private static IReadOnlyList<ReceiptLine> BuildLines(Order order, bool isReprint, ReceiptPaperWidth paperWidth)
    {
        var width = paperWidth == ReceiptPaperWidth.Mm58 ? 30 : 42;
        var itemWidth = paperWidth == ReceiptPaperWidth.Mm58 ? 17 : 27;
        var quantityWidth = 4;
        var amountWidth = width - itemWidth - quantityWidth;
        var receiptDate = RestaurantTime.ToLocal(order.ClosedUtc ?? order.OpenedUtc);
        var lines = new List<ReceiptLine>
        {
            new(WpfReceiptPrinter.RestaurantHeading, ReceiptAlignment.Center, true, 11),
            new(WpfReceiptPrinter.GetInvoiceHeading(isReprint), ReceiptAlignment.Center, true, 9),
            new($"Invoice: {order.InvoiceNumber}", ReceiptAlignment.Left, false, 7),
            new($"Date: {receiptDate:dd-MM-yyyy}  Time: {receiptDate:HH:mm}", ReceiptAlignment.Left, false, 7),
            new($"Type: {order.Type}", ReceiptAlignment.Left, false, 7)
        };
        if (!string.IsNullOrWhiteSpace(order.ServerName)) lines.Add(new($"Server: {order.ServerName}", ReceiptAlignment.Left, false, 7));
        lines.Add(new(new string('-', width), ReceiptAlignment.Left, false, 7));
        lines.Add(new(FixedColumns("Item", "Qty", "Amount", itemWidth, quantityWidth, amountWidth), ReceiptAlignment.Left, true, 7));
        foreach (var group in WpfReceiptPrinter.GetReceiptGroups(order))
        {
            lines.Add(new(group.Heading, ReceiptAlignment.Left, true, 7));
            foreach (var item in group.Lines)
            {
                var chunks = Wrap(item.ItemName, itemWidth).ToList();
                lines.Add(new(FixedColumns(chunks[0], FormatQuantity(item.Quantity), item.LineTotal.ToString("N2", CultureInfo.CurrentCulture), itemWidth, quantityWidth, amountWidth), ReceiptAlignment.Left, false, 7));
                foreach (var continuation in chunks.Skip(1)) lines.Add(new(continuation, ReceiptAlignment.Left, false, 7));
            }
        }
        var subtotal = decimal.Round(order.Lines.Sum(x => x.UnitPrice * x.Quantity), 2, MidpointRounding.AwayFromZero);
        lines.Add(new(new string('-', width), ReceiptAlignment.Left, false, 7));
        lines.Add(new($"Subtotal: {subtotal:N2}", ReceiptAlignment.Right, false, 7));
        if (order.DiscountAmount > 0) lines.Add(new($"Bill Discount: {order.DiscountAmount:N2}", ReceiptAlignment.Right, false, 7));
        lines.Add(new($"GST/Tax ({order.GstRate:N2}%): {order.TaxAmount:N2}", ReceiptAlignment.Right, false, 7));
        lines.Add(new($"Total Amount: {order.GrandTotal:N2}", ReceiptAlignment.Center, true, 9));
        if (order.Payments.Count > 0) lines.Add(new($"Payment: {string.Join(", ", order.Payments.Select(x => $"{x.Method} {x.Amount:N2}"))}", ReceiptAlignment.Center, false, 7));
        lines.Add(new("Thank you for dining with us", ReceiptAlignment.Center, true, 7));
        return lines;
    }

    private static string FixedColumns(string item, string quantity, string amount, int itemWidth, int quantityWidth, int amountWidth) =>
        LeftCell(item, itemWidth) + RightCell(quantity, quantityWidth) + RightCell(amount, amountWidth);

    private static string LeftCell(string value, int width) => value.Length > width ? value[..width] : value.PadRight(width);
    private static string RightCell(string value, int width) => value.Length > width ? value[^width..] : value.PadLeft(width);

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) { yield return string.Empty; yield break; }
        for (var index = 0; index < text.Length; index += width) yield return text.Substring(index, Math.Min(width, text.Length - index));
    }

    private static string FormatQuantity(decimal quantity) => quantity % 1m == 0 ? quantity.ToString("N0") : quantity.ToString("0.##");
    private static string Escape(string value) => new string(value.Select(character => character is >= ' ' and <= '~' ? character : '?').ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private sealed record ReceiptLine(string Text, ReceiptAlignment Alignment, bool Bold, double FontSize);
    private enum ReceiptAlignment { Left, Center, Right }
}
