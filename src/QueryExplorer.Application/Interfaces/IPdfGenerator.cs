using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IPdfGenerator
{
    /// <summary>
    /// Renders one PDF for a single result row, placing each field at its configured
    /// position. Table fields split their value (JSON array, or comma/semicolon/newline
    /// separated text) into rows of a small table and add a PDF bookmark pointing to it.
    /// If backgroundPdfBytes is provided, fields are drawn on top of that PDF's first page
    /// instead of a blank page.
    /// </summary>
    byte[] Generate(PdfLayoutTemplate layout, IReadOnlyDictionary<string, object?> rowData, byte[]? backgroundPdfBytes = null);
}
