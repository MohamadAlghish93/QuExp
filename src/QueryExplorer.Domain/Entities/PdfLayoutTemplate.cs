namespace QueryExplorer.Domain.Entities;

/// <summary>
/// A PDF layout tied to one favorite/template query - defines where each result
/// column's value is placed on the page when generating a PDF for a single row.
/// </summary>
public class PdfLayoutTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid FavoriteId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;

    /// <summary>"A4" or "Letter" - determines page dimensions used both by the designer preview and the generator.</summary>
    public string PageSize { get; set; } = "A4";

    public List<PdfFieldPlacement> Fields { get; set; } = new();

    /// <summary>Tables filled from a related ("sub") query - e.g. an order's line items - drawn into the PDF.</summary>
    public List<PdfTableBlock> Tables { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One field's position on the page, in PDF points measured from the top-left corner.
/// IsTable=true means the column's value is a delimited list (comma/semicolon/newline
/// separated, or a JSON array) rendered as a small table at this position, with a PDF
/// bookmark added pointing to it - rather than a single line of text.
/// </summary>
public class PdfFieldPlacement
{
    public string ColumnName { get; set; } = string.Empty;
    public string? Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double FontSize { get; set; } = 11;
    public bool IsTable { get; set; }
}

/// <summary>
/// A table printed from a second query related to the row's own: one parent row, many child rows
/// (an order and its lines, a customer and their invoices). The sub-query's {{placeholders}} are filled
/// from the parent row - a placeholder named like a parent column takes that column's value, anything
/// else takes ParentKeyColumn's. Each printed column has a header the designer typed in.
/// </summary>
public class PdfTableBlock
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Connection the sub-query runs on. Null = the same one as the template itself.</summary>
    public Guid? ConnectionId { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string ParentKeyColumn { get; set; } = string.Empty;

    public double X { get; set; } = 40;
    public double Y { get; set; } = 300;
    public double Width { get; set; } = 515;
    public double FontSize { get; set; } = 10;

    /// <summary>Columns to print, in order. Empty = every column the sub-query returns.</summary>
    public List<PdfTableColumn> Columns { get; set; } = new();
}

public class PdfTableColumn
{
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>Header text shown above the column - typed by whoever designs the layout.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Column width in points. Null = share the remaining width equally.</summary>
    public double? Width { get; set; }
}
