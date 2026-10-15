using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;
using System.Globalization;
using System.Text.Json;

namespace QueryExplorer.Infrastructure.Services;

/// <summary>
/// Renders one PDF for a single result row from a PdfLayoutTemplate's field
/// placements. Simple fields draw as "Label" then "value" (as two separate draw
/// calls - see the comment on DrawLabelledField for why) at their configured X/Y.
/// Table fields split their value into a list and render it as rows of a small
/// table, with a PDF bookmark (outline entry) added pointing to it. If the layout
/// has an uploaded background PDF, its first page is used instead of a blank page.
/// </summary>
public class PdfSharpCorePdfGenerator : IPdfGenerator
{
    private const double PageMarginBottom = 40;

    public byte[] Generate(PdfLayoutTemplate layout, IReadOnlyDictionary<string, object?> rowData, byte[]? backgroundPdfBytes = null)
    {
        var document = new PdfDocument();
        PdfPage page;

        if (backgroundPdfBytes is { Length: > 0 })
        {
            // Import just the first page of the uploaded PDF as this page's background -
            // additional pages in the uploaded file, if any, are ignored (one row = one page).
            using var backgroundStream = new MemoryStream(backgroundPdfBytes);
            var backgroundDoc = PdfReader.Open(backgroundStream, PdfDocumentOpenMode.Import);
            page = document.AddPage(backgroundDoc.Pages[0]); // copies the page into our new document
        }
        else
        {
            page = document.AddPage();
            page.Size = layout.PageSize.Equals("Letter", StringComparison.OrdinalIgnoreCase)
                ? PdfSharpCore.PageSize.Letter
                : PdfSharpCore.PageSize.A4;
        }

        // XGraphics.FromPdfPage on a page that already has content (an imported background)
        // appends to it rather than replacing it - this is what lets fields draw on top.
        var gfx = XGraphics.FromPdfPage(page);
        var brush = XBrushes.Black;

        foreach (var field in layout.Fields.Where(f => !f.IsTable))
        {
            rowData.TryGetValue(field.ColumnName, out var value);
            DrawLabelledField(gfx, field, value, brush);
        }

        foreach (var field in layout.Fields.Where(f => f.IsTable))
        {
            rowData.TryGetValue(field.ColumnName, out var value);
            var items = SplitListValue(value);

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

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>
    /// Draws the label and value as two SEPARATE DrawString calls rather than one
    /// concatenated "Label: value" string. ArabicTextShaper reorders a whole line for
    /// right-to-left reading, which is correct for a pure Arabic sentence - but a mixed
    /// "English Label: [Arabic value]" isn't one RTL sentence, it's an LTR label followed
    /// by an RTL value. Shaping the concatenated string as one line would swap the label
    /// and value's visual order. Drawing them separately, with only the value shaped,
    /// keeps the label where it belongs and the value's Arabic reads correctly on its own.
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

    private string FormatFieldValue(object rawValue, string formatMask)
    {
        if (rawValue == null || rawValue == DBNull.Value)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(formatMask))
        {
            if (rawValue is DateTime dt)
            {
                return dt.ToString(formatMask);
            }
            if (DateTime.TryParse(rawValue.ToString(), out DateTime parsedDate))
            {
                return parsedDate.ToString(formatMask);
            }
            if (rawValue is IFormattable formattable)
            {
                return formattable.ToString(formatMask, CultureInfo.InvariantCulture);
            }
        }

        return rawValue.ToString();
    }

    // Example of where to call it during generation:
    // string textToDraw = FormatFieldValue(field.Value, field.FormatMask);
    // gfx.DrawString(textToDraw, font, XBrushes.Black, new XRect(field.X, field.Y, field.Width, field.Height), XStringFormats.TopLeft);
}
