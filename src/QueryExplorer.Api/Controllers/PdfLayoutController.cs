using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Validation;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

/// <summary>
/// Defines and generates per-row PDFs for a favorite/template query. Designing a
/// layout (creating/editing field placements) requires Editor/Admin, same as
/// templates themselves; generating a PDF from an already-defined layout is open
/// to anyone who can see the underlying favorite/template.
/// </summary>
[ApiController]
[Route("api/pdf-layouts")]
[Authorize]
public class PdfLayoutController : ControllerBase
{
    private readonly IPdfLayoutRepository _layouts;
    private readonly IFavoriteQueryRepository _favorites;
    private readonly IPdfGenerator _generator;
    private readonly IConnectionRepository _connections;
    private readonly IQueryProviderFactory _providerFactory;
    private readonly IConnectionStringResolver _resolver;

    /// <summary>A PDF table is a printout, not a report - this keeps one runaway sub-query from producing a thousand pages.</summary>
    private const int MaxTableRows = 500;

    public PdfLayoutController(
        IPdfLayoutRepository layouts, IFavoriteQueryRepository favorites, IPdfGenerator generator,
        IConnectionRepository connections, IQueryProviderFactory providerFactory, IConnectionStringResolver resolver)
    {
        _layouts = layouts;
        _favorites = favorites;
        _generator = generator;
        _connections = connections;
        _providerFactory = providerFactory;
        _resolver = resolver;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";
    private bool CanEditTemplates => Role is "Admin" or "Editor";

    // Uploaded background PDFs live alongside the other per-feature data files/folders
    // under the app's data directory, one file per layout id.
    private static readonly string BackgroundsDirectory = Path.Combine(AppContext.BaseDirectory, "data", "pdf_backgrounds");
    private static string BackgroundPath(Guid layoutId) => Path.Combine(BackgroundsDirectory, $"{layoutId}.pdf");

    [HttpGet]
    public async Task<ActionResult<List<PdfLayoutResponse>>> GetForFavorite([FromQuery] Guid favoriteId)
    {
        var layouts = await _layouts.GetForFavoriteAsync(favoriteId);
        return Ok(layouts.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PdfLayoutResponse>> GetById(Guid id)
    {
        var layout = await _layouts.GetByIdAsync(id);
        return layout is null ? NotFound() : Ok(ToResponse(layout));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<ActionResult<PdfLayoutResponse>> Save([FromBody] SavePdfLayoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });
        if (request.Fields.Count == 0) return BadRequest(new { error = "Add at least one field to the layout." });

        var favorite = await _favorites.GetByIdAsync(request.FavoriteId);
        if (favorite is null) return BadRequest(new { error = "Unknown template." });

        // Designing a layout is tied to the template it belongs to: only its owner or an Admin
        // can add one - not just any Editor, as before.
        var ownsTemplate = favorite.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase);
        if (!ownsTemplate && Role != "Admin")
            return StatusCode(403, new { error = "Only the template's owner or an Admin can design its PDF layout." });

        // Each table's sub-query goes through the same checks as any query the app runs.
        if (request.Tables.Count > 10) return BadRequest(new { error = "A layout can have at most 10 tables." });
        foreach (var table in request.Tables)
        {
            var label = string.IsNullOrWhiteSpace(table.Title) ? "A table" : $"Table \"{table.Title}\"";
            var (sqlOk, sqlError) = SqlValidator.Validate(table.Sql);
            if (!sqlOk) return BadRequest(new { error = $"{label}: {sqlError}" });
            if (table.Width <= 0) return BadRequest(new { error = $"{label} needs a width greater than zero." });

            if (table.ConnectionId.HasValue)
            {
                var connection = await _connections.GetByIdAsync(table.ConnectionId.Value);
                if (connection is null) return BadRequest(new { error = $"{label}: that connection doesn't exist." });
                if (!connection.CanAccess(Username, Role))
                    return StatusCode(403, new { error = $"{label}: you don't have access to that connection." });
            }
        }

        var existing = request.Id.HasValue ? await _layouts.GetByIdAsync(request.Id.Value) : null;
        if (existing is not null && !existing.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase) && Role != "Admin")
            return Forbid();

        var layout = new PdfLayoutTemplate
        {
            Id = existing?.Id ?? request.Id ?? Guid.NewGuid(),
            Name = request.Name.Trim(),
            FavoriteId = request.FavoriteId,
            OwnerUsername = existing?.OwnerUsername ?? Username,
            PageSize = request.PageSize == "Letter" ? "Letter" : "A4",
            Fields = request.Fields.Select(f => new PdfFieldPlacement
            {
                ColumnName = f.ColumnName,
                Label = f.Label,
                X = f.X,
                Y = f.Y,
                FontSize = f.FontSize <= 0 ? 11 : f.FontSize,
                IsTable = f.IsTable
            }).ToList(),
            Tables = request.Tables.Select(t => new PdfTableBlock
            {
                Title = t.Title?.Trim() ?? "",
                ConnectionId = t.ConnectionId,
                Sql = t.Sql.Trim(),
                ParentKeyColumn = t.ParentKeyColumn?.Trim() ?? "",
                X = t.X, Y = t.Y, Width = t.Width,
                FontSize = t.FontSize <= 0 ? 10 : t.FontSize,
                Columns = t.Columns.Select(c => new PdfTableColumn
                {
                    ColumnName = c.ColumnName, Header = c.Header?.Trim() ?? "", Width = c.Width is > 0 ? c.Width : null
                }).ToList()
            }).ToList(),
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow
        };

        var saved = await _layouts.SaveAsync(layout);
        return Ok(ToResponse(saved));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var layout = await _layouts.GetByIdAsync(id);
        if (layout is null) return NotFound();
        if (!layout.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase) && Role != "Admin")
            return Forbid();

        var removed = await _layouts.DeleteAsync(id);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>Uploads (or replaces) the background PDF used behind this layout's fields -
    /// e.g. a company's official invoice or certificate template. Only its first page is used.</summary>
    [HttpPost("{id:guid}/background")]
    [Authorize(Roles = "Admin,Editor")]
    [RequestSizeLimit(20_000_000)] // 20 MB - generous for a one/two-page template PDF
    public async Task<IActionResult> UploadBackground(Guid id, IFormFile file)
    {
        var layout = await _layouts.GetByIdAsync(id);
        if (layout is null) return NotFound();
        if (!layout.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase) && Role != "Admin")
            return Forbid();

        if (file is null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });
        if (!file.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only PDF files are accepted." });

        Directory.CreateDirectory(BackgroundsDirectory);
        await using var destination = System.IO.File.Create(BackgroundPath(id));
        await file.CopyToAsync(destination);

        return NoContent();
    }

    /// <summary>Streams back the uploaded background PDF, for the designer's preview and for generation.</summary>
    [HttpGet("{id:guid}/background")]
    public IActionResult GetBackground(Guid id)
    {
        var path = BackgroundPath(id);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf");
    }

    [HttpDelete("{id:guid}/background")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<IActionResult> DeleteBackground(Guid id)
    {
        var layout = await _layouts.GetByIdAsync(id);
        if (layout is null) return NotFound();
        if (!layout.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase) && Role != "Admin")
            return Forbid();

        var path = BackgroundPath(id);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        return NoContent();
    }

    /// <summary>Generates a PDF for one result row using this layout. The row's data comes
    /// from the client (it already has it from the query result grid) rather than re-running
    /// the query server-side, so the PDF always matches exactly what the user is looking at.</summary>
    [HttpPost("{id:guid}/generate")]
    public async Task<IActionResult> Generate(Guid id, [FromBody] GeneratePdfRequest request, CancellationToken ct)
    {
        var layout = await _layouts.GetByIdAsync(id);
        if (layout is null) return NotFound();

        byte[]? backgroundBytes = null;
        var backgroundPath = BackgroundPath(id);
        if (System.IO.File.Exists(backgroundPath))
            backgroundBytes = await System.IO.File.ReadAllBytesAsync(backgroundPath);

        // The template's date field formats previously only applied to the on-screen grid -
        // the PDF drew the raw value. Applying them here keeps both in sync.
        var favorite = await _favorites.GetByIdAsync(layout.FavoriteId);
        var rowData = favorite is { DateFieldFormats.Count: > 0 }
            ? QueryExplorer.Application.Formatting.DateFieldFormatter.ApplyFormats(request.RowData, favorite.DateFieldFormats)
            : request.RowData;

        // Fetch each sub-query table for THIS row. Parameters come from the untouched row values
        // (not the date-formatted copy), so a key is never altered on its way into the query.
        var tables = new List<PdfTableData>();
        foreach (var block in layout.Tables)
        {
            var (table, tableError, status) = await LoadTableAsync(block, favorite, request.RowData, ct);
            if (table is null) return StatusCode(status, new { error = tableError });
            tables.Add(table);
        }

        byte[] pdfBytes;
        try
        {
            pdfBytes = _generator.Generate(layout, rowData, backgroundBytes, tables);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. no font file found on this server - surface as a real error, not a generic 500.
            return StatusCode(500, new { error = ex.Message });
        }

        return File(pdfBytes, "application/pdf", $"{layout.Name}.pdf");
    }

    /// <summary>
    /// Runs a table's sub-query for one parent row. A {{placeholder}} named like a parent column takes that
    /// column's value; any other placeholder takes the value of the block's ParentKeyColumn. Values are bound
    /// as real database parameters, never pasted into the SQL.
    /// </summary>
    private async Task<(PdfTableData? Table, string? Error, int Status)> LoadTableAsync(
        PdfTableBlock block, FavoriteQuery? favorite, IReadOnlyDictionary<string, object?> parentRow, CancellationToken ct)
    {
        var label = string.IsNullOrWhiteSpace(block.Title) ? "A table" : $"Table \"{block.Title}\"";

        var connectionId = block.ConnectionId ?? favorite?.ConnectionId;
        var connection = connectionId.HasValue ? await _connections.GetByIdAsync(connectionId.Value) : null;
        if (connection is null) return (null, $"{label} uses a connection that no longer exists.", 400);
        if (!connection.CanAccess(Username, Role))
            return (null, $"{label} reads from a connection you don't have access to.", 403);

        var (sqlOk, sqlError) = SqlValidator.Validate(block.Sql);
        if (!sqlOk) return (null, $"{label}: {sqlError}", 400);

        string? ValueOf(string column)
        {
            var key = parentRow.Keys.FirstOrDefault(k => string.Equals(k, column, StringComparison.OrdinalIgnoreCase));
            return key is null ? null : parentRow[key]?.ToString();
        }

        var parameters = new Dictionary<string, object?>();
        foreach (var name in QueryParameterHelper.ExtractParameterNames(block.Sql))
        {
            var value = ValueOf(name) ?? (string.IsNullOrWhiteSpace(block.ParentKeyColumn) ? null : ValueOf(block.ParentKeyColumn));
            if (value is null)
                return (null, $"{label}: there's no value for {{{{{name}}}}} in this row. Pick which column of the main query supplies it.", 400);
            parameters[name] = value;
        }

        try
        {
            var provider = _providerFactory.GetProvider(connection.ProviderType);
            var (columns, rows) = await provider.ExecutePageAsync(_resolver.Resolve(connection), block.Sql, 0, MaxTableRows, parameters, ct);

            // Print the chosen columns (matched to the real column names ignoring case), or all of them if none were chosen.
            var printed = block.Columns.Count == 0
                ? columns.Select(c => new PdfTableColumn { ColumnName = c, Header = c }).ToList()
                : block.Columns
                    .Select(c => new { Column = c, Actual = columns.FirstOrDefault(x => string.Equals(x, c.ColumnName, StringComparison.OrdinalIgnoreCase)) })
                    .Where(x => x.Actual is not null)
                    .Select(x => new PdfTableColumn { ColumnName = x.Actual!, Header = x.Column.Header, Width = x.Column.Width })
                    .ToList();

            return (new PdfTableData { Block = block, Columns = printed, Rows = rows }, null, 200);
        }
        catch (Exception ex)
        {
            return (null, $"{label}: {ex.Message}", 400);
        }
    }

    private static PdfLayoutResponse ToResponse(PdfLayoutTemplate layout) => new()
    {
        Id = layout.Id,
        Name = layout.Name,
        FavoriteId = layout.FavoriteId,
        OwnerUsername = layout.OwnerUsername,
        PageSize = layout.PageSize,
        HasBackgroundPdf = System.IO.File.Exists(BackgroundPath(layout.Id)),
        Fields = layout.Fields.Select(f => new PdfFieldPlacementDto
        {
            ColumnName = f.ColumnName,
            Label = f.Label,
            X = f.X,
            Y = f.Y,
            FontSize = f.FontSize,
            IsTable = f.IsTable
        }).ToList(),
        Tables = layout.Tables.Select(t => new PdfTableBlockDto
        {
            Title = t.Title, ConnectionId = t.ConnectionId, Sql = t.Sql, ParentKeyColumn = t.ParentKeyColumn,
            X = t.X, Y = t.Y, Width = t.Width, FontSize = t.FontSize,
            Columns = t.Columns.Select(c => new PdfTableColumnDto { ColumnName = c.ColumnName, Header = c.Header, Width = c.Width }).ToList()
        }).ToList()
    };
}
