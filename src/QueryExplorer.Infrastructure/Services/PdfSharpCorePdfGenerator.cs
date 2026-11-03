using System.Globalization;
using System.Text.Json;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Services;

/// <summary>
/// Renders one PDF for a single result row from a PdfLayoutTemplate: simple fields at their X/Y,
/// multi-value columns as bulleted lists (with a bookmark), and sub-query tables as real bordered
/// tables with the headers the designer typed. If the layout has an uploaded background PDF its first
/// page is used for the first page - and for every continuation page a long table needs.
/// </summary>
public class PdfSharpCorePdfGenerator : IPdfGenerator
{
    private const double PageMarginTop = 40;
    private const double PageMarginBottom = 40;

    public byte[] Generate(PdfLayoutTemplate layout, IReadOnlyDictionary<string, object?> rowData,
        byte[]? backgroundPdfBytes = null, IReadOnlyList<PdfTableData>? tables = null)
    {
        var document = new PdfDocument();

        // The imported background has to stay open for the whole run: continuation pages import from it later.
        MemoryStream? backgroundStream = null;
        PdfDocument? backgroundDoc = null;
        if (backgroundPdfBytes is { Length: > 0 })
        {
            backgroundStream = new MemoryStream(backgroundPdfBytes);
            backgroundDoc = PdfReader.Open(backgroundStream, PdfDocumentOpenMode.Import);
        }

        try
        {
            var pageSize = layout.PageSize.Equals("Letter", StringComparison.OrdinalIgnoreCase)
                ? PdfSharpCore.PageSize.Letter : PdfSharpCore.PageSize.A4;

            PdfPage NewPage()
            {
                if (backgroundDoc is not null) return document.AddPage(backgroundDoc.Pages[0]); // copies the background page
                var blank = document.AddPage();
                blank.Size = pageSize;
                return blank;
            }

            var page = NewPage();
            var gfx = XGraphics.FromPdfPage(page); // appends to an imported background rather than replacing it
            var brush = XBrushes.Black;

            foreach (var field in layout.Fields.Where(f => !f.IsTable))
            {
                rowData.TryGetValue(field.ColumnName, out var value);
                DrawLabelledField(gfx, field, value, brush);
            }

            foreach (var field in layout.Fields.Where(f => f.IsTable))
            {
                rowData.TryGetValue(field.ColumnName, out var value);
                DrawBulletList(document, page, gfx, field, SplitListValue(value), brush);
            }

            // Tables can run past the page, so each one may swap in a new page + graphics context.
            foreach (var table in tables ?? Array.Empty<PdfTableData>())
            {
                (page, gfx) = DrawTable(document, page, gfx, table, NewPage);
            }

            using var stream = new MemoryStream();
            document.Save(stream, false);
            return stream.ToArray();
        }
        finally
        {
            backgroundStream?.Dispose();
        }
    }

    // ---------------------------------------------------------------- simple fields and lists

    /// <summary>
    /// Label and value are two SEPARATE draws: ArabicTextShaper reorders a whole line right-to-left, which is
    /// right for a pure Arabic sentence but would swap "English label" and "Arabic value" if shaped together.
    /// </summary>
    private static void DrawLabelledField(XGraphics gfx, PdfFieldPlacement field, object? value, XBrush brush)
    {
        var font = new XFont("DefaultFont", field.FontSize);
        var valueText = ArabicTextShaper.PrepareForDisplay(value?.ToString() ?? "");

        if (string.IsNullOrEmpty(field.Label))
        {
            gfx.DrawString(valueText, font, brush, new XPoint(field.X, field.Y));
            return;
        }

        var labelText = field.Label + ": ";
        gfx.DrawString(labelText, font, brush, new XPoint(field.X, field.Y));
        var labelWidth = gfx.MeasureString(labelText, font).Width;
        gfx.DrawString(valueText, font, brush, new XPoint(field.X + labelWidth, field.Y));
    }

    private static void DrawBulletList(PdfDocument document, PdfPage page, XGraphics gfx,
        PdfFieldPlacement field, List<string> items, XBrush brush)
    {
        document.Outlines.Add(field.Label ?? field.ColumnName, page, true);

        var headerFont = new XFont("DefaultFontBold", field.FontSize, XFontStyle.Bold);
        var rowFont = new XFont("DefaultFont", field.FontSize);
        var rowHeight = field.FontSize + 6;

        gfx.DrawString(field.Label ?? field.ColumnName, headerFont, brush, new XPoint(field.X, field.Y));
        var y = field.Y + rowHeight;

        var maxY = page.Height.Point - PageMarginBottom;
        var shown = 0;
        foreach (var item in items)
        {
            if (y > maxY)
            {
                gfx.DrawString($"… and {items.Count - shown} more", rowFont, brush, new XPoint(field.X + 10, y));
                break;
            }
            gfx.DrawString("• " + ArabicTextShaper.PrepareForDisplay(item), rowFont, brush, new XPoint(field.X + 10, y));
            y += rowHeight;
            shown++;
        }
    }

    // ---------------------------------------------------------------- sub-query tables

    private static (PdfPage Page, XGraphics Gfx) DrawTable(PdfDocument document, PdfPage page, XGraphics gfx,
        PdfTableData table, Func<PdfPage> newPage)
    {
        var block = table.Block;
        var headerFont = new XFont("DefaultFontBold", block.FontSize, XFontStyle.Bold);
        var cellFont = new XFont("DefaultFont", block.FontSize);
        var rowHeight = block.FontSize + 9;
        var pad = 4.0;
        var border = new XPen(XColors.Gray, 0.5);
        var headerFill = new XSolidBrush(XColor.FromArgb(235, 238, 247));
        var widths = ColumnWidths(table.Columns, block.Width);

        var y = block.Y;

        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            document.Outlines.Add(block.Title, page, true); // bookmark pointing at the table

            // An Arabic title belongs on the right-hand edge of the table, anything else on the left.
            // Aligned to the table's real width (its columns' total), so it lines up with the table itself.
            var tableWidth = widths.Sum() > 0 ? widths.Sum() : block.Width;
            var titleFormat = new XStringFormat
            {
                Alignment = ArabicTextShaper.ContainsArabic(block.Title) ? XStringAlignment.Far : XStringAlignment.Near,
                LineAlignment = XLineAlignment.Center
            };
            gfx.DrawString(ArabicTextShaper.PrepareForDisplay(block.Title), headerFont, XBrushes.Black,
                new XRect(block.X, y, tableWidth, rowHeight), titleFormat);
            y += rowHeight;
        }

        void DrawHeaderRow()
        {
            var x = block.X;
            for (var i = 0; i < table.Columns.Count; i++)
            {
                gfx.DrawRectangle(border, headerFill, x, y, widths[i], rowHeight);
                var header = string.IsNullOrWhiteSpace(table.Columns[i].Header) ? table.Columns[i].ColumnName : table.Columns[i].Header;
                gfx.DrawString(Fit(gfx, ArabicTextShaper.PrepareForDisplay(header), headerFont, widths[i] - 2 * pad),
                    headerFont, XBrushes.Black, new XRect(x + pad, y, widths[i] - 2 * pad, rowHeight), XStringFormats.CenterLeft);
                x += widths[i];
            }
            y += rowHeight;
        }

        DrawHeaderRow();

        if (table.Rows.Count == 0)
        {
            gfx.DrawRectangle(border, block.X, y, widths.Sum(), rowHeight);
            gfx.DrawString("No records", cellFont, XBrushes.Gray,
                new XRect(block.X + pad, y, widths.Sum() - 2 * pad, rowHeight), XStringFormats.CenterLeft);
            return (page, gfx);
        }

        foreach (var row in table.Rows)
        {
            if (y + rowHeight > page.Height.Point - PageMarginBottom)
            {
                // Out of room: carry on at the top of a fresh page, repeating the header so it still reads as a table.
                gfx.Dispose();
                page = newPage();
                gfx = XGraphics.FromPdfPage(page);
                y = PageMarginTop;
                DrawHeaderRow();
            }

            var x = block.X;
            for (var i = 0; i < table.Columns.Count; i++)
            {
                gfx.DrawRectangle(border, x, y, widths[i], rowHeight);
                row.TryGetValue(table.Columns[i].ColumnName, out var value);
                var text = Fit(gfx, ArabicTextShaper.PrepareForDisplay(FormatCell(value)), cellFont, widths[i] - 2 * pad);
                gfx.DrawString(text, cellFont, XBrushes.Black,
                    new XRect(x + pad, y, widths[i] - 2 * pad, rowHeight), XStringFormats.CenterLeft);
                x += widths[i];
            }
            y += rowHeight;
        }

        return (page, gfx);
    }

    /// <summary>Columns with an explicit width keep it; the rest share what's left of the table width equally.</summary>
    private static double[] ColumnWidths(List<PdfTableColumn> columns, double totalWidth)
    {
        var explicitTotal = columns.Where(c => c.Width is > 0).Sum(c => c.Width!.Value);
        var flexCount = columns.Count(c => c.Width is not > 0);
        var flexWidth = flexCount == 0 ? 0 : Math.Max(30, (totalWidth - explicitTotal) / flexCount);
        return columns.Select(c => c.Width is > 0 ? c.Width!.Value : flexWidth).ToArray();
    }

    /// <summary>Trims text with an ellipsis until it fits the cell, so a long value never spills into the next column.</summary>
    private static string Fit(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return "";
        if (gfx.MeasureString(text, font).Width <= maxWidth) return text;

        var length = text.Length;
        while (length > 1 && gfx.MeasureString(text[..length] + "…", font).Width > maxWidth) length--;
        return text[..length] + "…";
    }

    private static string FormatCell(object? value) => value switch
    {
        null => "",
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                                                     : dt.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>Accepts either a JSON array (e.g. a column holding '["a","b"]') or a plain
    /// delimited string (comma/semicolon/newline separated) and returns the individual items.</summary>
    private static List<string> SplitListValue(object? value)
    {
        var raw = value?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<JsonElement>>(raw);
            if (parsed is { Count: > 0 })
                return parsed.Select(e => e.ToString()).ToList();
        }
        catch (JsonException)
        {
            // Not JSON - fall through to delimiter splitting.
        }

        return raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => s.Length > 0)
                   .ToList();
    }
}
