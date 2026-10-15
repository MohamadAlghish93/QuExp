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
