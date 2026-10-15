namespace QueryExplorer.Application.DTOs;

public class PdfFieldPlacementDto
{
    public string ColumnName { get; set; } = string.Empty;
    public string? Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double FontSize { get; set; } = 11;
    public bool IsTable { get; set; }
}

public class SavePdfLayoutRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid FavoriteId { get; set; }
    public string PageSize { get; set; } = "A4";
    public List<PdfFieldPlacementDto> Fields { get; set; } = new();
}

public class PdfLayoutResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid FavoriteId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;
    public string PageSize { get; set; } = "A4";
    public bool HasBackgroundPdf { get; set; }
    public List<PdfFieldPlacementDto> Fields { get; set; } = new();
}

public class GeneratePdfRequest
{
    public Dictionary<string, object?> RowData { get; set; } = new();
}

/// <summary>Response for the Templates page side panel - name + count, so a slow count
/// on one template doesn't block showing the others.</summary>
public class TemplateRecordCountResponse
{
    public Guid FavoriteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? Count { get; set; }
    public string? Error { get; set; }
}
