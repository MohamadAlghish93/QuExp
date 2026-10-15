using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
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

    public PdfLayoutController(IPdfLayoutRepository layouts, IFavoriteQueryRepository favorites, IPdfGenerator generator)
    {
        _layouts = layouts;
        _favorites = favorites;
        _generator = generator;
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
    public async Task<IActionResult> Generate(Guid id, [FromBody] GeneratePdfRequest request)
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

        byte[] pdfBytes;
        try
        {
            pdfBytes = _generator.Generate(layout, rowData, backgroundBytes);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. no font file found on this server - surface as a real error, not a generic 500.
            return StatusCode(500, new { error = ex.Message });
        }

        return File(pdfBytes, "application/pdf", $"{layout.Name}.pdf");
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
        }).ToList()
    };
}
