using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Validation;
using QueryExplorer.Domain.Entities;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IFavoriteQueryRepository _favorites;
    private readonly IAuditLogRepository _auditLog;
    private readonly IConnectionRepository _connections;
    private readonly IQueryProviderFactory _providerFactory;
    private readonly IConnectionStringResolver _resolver;

    public FavoritesController(
        IFavoriteQueryRepository favorites, IAuditLogRepository auditLog,
        IConnectionRepository connections, IQueryProviderFactory providerFactory, IConnectionStringResolver resolver)
    {
        _favorites = favorites;
        _auditLog = auditLog;
        _connections = connections;
        _providerFactory = providerFactory;
        _resolver = resolver;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";
    private bool CanEditTemplates => Role is "Admin" or "Editor";

    /// <summary>
    /// templatesOnly=true -> shared Templates library, filtered to what's visible to the
    /// caller (shared with everyone, shared specifically with them, or owned by them).
    /// templatesOnly=false or omitted -> caller's own personal favorites (the Favorites page).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<FavoriteQuery>>> Search(
        [FromQuery] string? search, [FromQuery] string? category, [FromQuery] bool? templatesOnly)
    {
        // Admins AND Editors see every template regardless of sharing - both roles curate
        // the shared library, so an Editor needs to find and edit (e.g. remove a shared
        // person from) a template even if they didn't create it or aren't in its share list.
        // Without this, a non-owner Editor couldn't even see such a template to edit it -
        // which is exactly what "can't remove the shared person" looks like from the outside.
        // Personal (non-template) favorites always stay scoped to their owner regardless -
        // this bypass never applies there, or it would leak private saved queries.
        var isTemplatesRequest = templatesOnly == true;
        var ownerForVisibility = (CanEditTemplates && isTemplatesRequest) ? null : Username;
        return Ok(await _favorites.SearchAsync(search, category, templatesOnly, ownerForVisibility));
    }

    /// <summary>
    /// Row count for every template visible to the caller, for the Templates page's side
    /// panel. Runs all counts in parallel and isolates failures per item - one slow or
    /// unreachable connection shouldn't block the count for every other template.
    /// </summary>
    [HttpGet("record-counts")]
    public async Task<ActionResult<List<TemplateRecordCountResponse>>> GetRecordCounts(CancellationToken ct)
    {
        var ownerForVisibility = CanEditTemplates ? null : Username;
        var templates = await _favorites.SearchAsync(null, null, true, ownerForVisibility);
        var connections = await _connections.GetAllAsync();

        // A template whose connection the caller can't use shouldn't appear here at all -
        // a dash just tells them something exists that they can't reach.
        var accessible = templates.Where(t =>
        {
            var c = connections.FirstOrDefault(x => x.Id == t.ConnectionId);
            return c is not null && c.CanAccess(Username, Role);
        });

        var tasks = accessible.Select(async template =>
        {
            var connection = connections.First(c => c.Id == template.ConnectionId);

            try
            {
                var requiredParams = QueryParameterHelper.ExtractParameterNames(template.Sql);
                if (requiredParams.Count > 0)
                    return new TemplateRecordCountResponse { FavoriteId = template.Id, Name = template.Name, Error = "Needs parameters - run it to see a count." };

                var provider = _providerFactory.GetProvider(connection.ProviderType);
                var connectionString = _resolver.Resolve(connection);
                var count = await provider.ExecuteCountAsync(connectionString, template.Sql, new Dictionary<string, object?>(), ct);
                return new TemplateRecordCountResponse { FavoriteId = template.Id, Name = template.Name, Count = count };
            }
            catch (Exception ex)
            {
                return new TemplateRecordCountResponse { FavoriteId = template.Id, Name = template.Name, Error = ex.Message };
            }
        });

        return Ok(await Task.WhenAll(tasks));
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories()
        => Ok(await _favorites.GetCategoriesAsync());

    [HttpGet("tags")]
    public async Task<ActionResult<List<string>>> GetTags()
        => Ok(await _favorites.GetTagsAsync());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FavoriteQuery>> GetById(Guid id)
    {
        var favorite = await _favorites.GetByIdAsync(id);
        return favorite is null ? NotFound() : Ok(favorite);
    }

    [HttpPost]
    public async Task<ActionResult<FavoriteQuery>> Save([FromBody] SaveFavoriteRequest request)
    {
        if (request.IsTemplate && !CanEditTemplates)
            return Forbid(); // Viewers can run templates but not create/edit them

        var existing = request.Id.HasValue ? await _favorites.GetByIdAsync(request.Id.Value) : null;
        // Only the owner (or an Editor/Admin, for templates) can overwrite an existing entry.
        // Entries saved before ownership tracking existed (empty OwnerUsername) are grandfathered as editable by anyone.
        if (existing is not null && !string.IsNullOrEmpty(existing.OwnerUsername)
            && !existing.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase)
            && !(existing.IsTemplate && CanEditTemplates))
            return Forbid();

        // Reuse the existing spelling/casing of a category or tag when one already
        // exists, so "Reports" and "reports" don't end up as two separate entries.
        var existingCategories = await _favorites.GetCategoriesAsync();
        var existingTags = await _favorites.GetTagsAsync();

        var category = string.IsNullOrWhiteSpace(request.Category) ? "Uncategorized" : request.Category.Trim();
        category = existingCategories.FirstOrDefault(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)) ?? category;

        var tags = (request.Tags ?? new List<string>())
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => existingTags.FirstOrDefault(e => e.Equals(t, StringComparison.OrdinalIgnoreCase)) ?? t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entity = new FavoriteQuery
        {
            Id = existing?.Id ?? request.Id ?? Guid.NewGuid(),
            Name = request.Name,
            Sql = request.Sql,
            ConnectionId = request.ConnectionId,
            Category = category,
            Tags = tags,
            OwnerUsername = existing?.OwnerUsername ?? Username,
            IsTemplate = request.IsTemplate,
            Description = request.Description,
            // Empty/omitted = shared with everyone (the original template behavior).
            SharedWithUsernames = request.IsTemplate ? (request.SharedWithUsernames ?? new List<string>()) : new List<string>(),
            // New templates from an Editor need Admin approval before appearing org-wide;
            // Admin-created templates auto-approve. Editing an existing template preserves
            // whatever approval status it already had - editing doesn't silently re-hide it,
            // and it also doesn't silently re-approve something an Admin rejected.
            ApprovalStatus = existing?.ApprovalStatus
                ?? (request.IsTemplate && Role != "Admin" ? TemplateApprovalStatus.Pending : TemplateApprovalStatus.Approved),
            DateFieldFormats = request.DateFieldFormats ?? existing?.DateFieldFormats ?? new List<DateFieldFormat>(),
            HiddenByUsernames = existing?.HiddenByUsernames ?? new List<string>(),
            // Workflow state is only ever changed through WorkflowController - an ordinary edit
            // must carry it forward untouched, or saving a template would silently drop it from its workflow.
            WorkflowDefinitionId = existing?.WorkflowDefinitionId,
            WorkflowName = existing?.WorkflowName,
            WorkflowSteps = existing?.WorkflowSteps ?? new List<WorkflowStep>(),
            WorkflowStepIndex = existing?.WorkflowStepIndex ?? 0,
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow
        };

        var saved = await _favorites.SaveAsync(entity);
        return Ok(saved);
    }

    [HttpPost("{id:guid}/use")]
    public async Task<IActionResult> MarkUsed(Guid id)
    {
        var favorite = await _favorites.GetByIdAsync(id);
        if (favorite is null) return NotFound();
        favorite.LastUsedAtUtc = DateTime.UtcNow;
        await _favorites.SaveAsync(favorite);
        return NoContent();
    }

    /// <summary>Renames a category everywhere it's used. Admin-only - this affects everyone's favorites.</summary>
    [HttpPut("categories/rename")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RenameCategory([FromBody] RenameCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OldName) || string.IsNullOrWhiteSpace(request.NewName))
            return BadRequest(new { error = "Both names are required." });

        var affected = await _favorites.RenameCategoryAsync(request.OldName.Trim(), request.NewName.Trim());
        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = Username, Action = "CategoryRenamed", TargetDescription = $"{request.OldName} -> {request.NewName} ({affected} favorite(s))" });
        return Ok(new { affected });
    }

    /// <summary>Renames a tag everywhere it's used. Admin-only - this affects everyone's favorites.</summary>
    [HttpPut("tags/rename")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RenameTag([FromBody] RenameTagRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OldName) || string.IsNullOrWhiteSpace(request.NewName))
            return BadRequest(new { error = "Both names are required." });

        var affected = await _favorites.RenameTagAsync(request.OldName.Trim(), request.NewName.Trim());
        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = Username, Action = "TagRenamed", TargetDescription = $"{request.OldName} -> {request.NewName} ({affected} favorite(s))" });
        return Ok(new { affected });
    }

    /// <summary>Approves a pending template, making it visible org-wide per its sharing settings. Admin-only.</summary>
    [HttpPut("{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(Guid id)
    {
        var favorite = await _favorites.GetByIdAsync(id);
        if (favorite is null || !favorite.IsTemplate) return NotFound();
        if (favorite.WorkflowSteps.Count > 0)
            return BadRequest(new { error = "This template is managed by its workflow - move it through the workflow steps instead." });

        favorite.ApprovalStatus = TemplateApprovalStatus.Approved;
        await _favorites.SaveAsync(favorite);
        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = Username, Action = "TemplateApproved", TargetDescription = favorite.Name });
        return NoContent();
    }

    /// <summary>Rejects a pending template - stays visible only to its owner, who can revise and it'll need re-approval. Admin-only.</summary>
    [HttpPut("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(Guid id)
    {
        var favorite = await _favorites.GetByIdAsync(id);
        if (favorite is null || !favorite.IsTemplate) return NotFound();
        if (favorite.WorkflowSteps.Count > 0)
            return BadRequest(new { error = "This template is managed by its workflow - send it back through the workflow instead." });

        favorite.ApprovalStatus = TemplateApprovalStatus.Rejected;
        await _favorites.SaveAsync(favorite);
        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = Username, Action = "TemplateRejected", TargetDescription = favorite.Name });
        return NoContent();
    }

    /// <summary>Templates awaiting approval, for the Admin review queue.</summary>
    [HttpGet("pending")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<FavoriteQuery>>> GetPending()
    {
        var all = await _favorites.SearchAsync(null, null, true, null); // Admin bypass - sees everything
        return Ok(all.Where(f => f.ApprovalStatus == TemplateApprovalStatus.Pending && f.WorkflowSteps.Count == 0).ToList());
    }

    /// <summary>Moves several favorites to a category at once. Each item is skipped (not failed outright)
    /// if the caller doesn't own it and it isn't an editable template, so one bad id doesn't block the rest.</summary>
    [HttpPut("bulk/category")]
    public async Task<IActionResult> BulkMoveCategory([FromBody] BulkCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Category)) return BadRequest(new { error = "Category is required." });

        var updated = 0;
        foreach (var id in request.Ids)
        {
            var favorite = await _favorites.GetByIdAsync(id);
            if (favorite is null || !CanModify(favorite)) continue;

            favorite.Category = request.Category.Trim();
            await _favorites.SaveAsync(favorite);
            updated++;
        }
        return Ok(new { updated });
    }

    /// <summary>Adds a tag to several favorites at once (existing tags are kept, not replaced).</summary>
    [HttpPut("bulk/tag")]
    public async Task<IActionResult> BulkAddTag([FromBody] BulkTagRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Tag)) return BadRequest(new { error = "Tag is required." });
        var tag = request.Tag.Trim();

        var updated = 0;
        foreach (var id in request.Ids)
        {
            var favorite = await _favorites.GetByIdAsync(id);
            if (favorite is null || !CanModify(favorite)) continue;

            if (!favorite.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                favorite.Tags.Add(tag);
            await _favorites.SaveAsync(favorite);
            updated++;
        }
        return Ok(new { updated });
    }

    /// <summary>Deletes several favorites at once.</summary>
    [HttpDelete("bulk")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
    {
        var deleted = 0;
        foreach (var id in request.Ids)
        {
            var favorite = await _favorites.GetByIdAsync(id);
            if (favorite is null || !CanModify(favorite)) continue;

            if (await _favorites.DeleteAsync(id)) deleted++;
        }
        return Ok(new { deleted });
    }

    /// <summary>Same ownership rule used by the single-item Save/Delete endpoints: owner, or an
    /// Editor/Admin for a template, or grandfathered (no owner recorded) entries.</summary>
    private bool CanModify(FavoriteQuery favorite) =>
        string.IsNullOrEmpty(favorite.OwnerUsername) ||
        favorite.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase) ||
        (favorite.IsTemplate && CanEditTemplates);

    /// <summary>
    /// Lets a user a template was shared with hide it from their own Templates view,
    /// without affecting the owner, anyone else it's shared with, or the share itself.
    /// The owner can't hide their own template this way - they have Delete for that.
    /// </summary>
    /// <summary>Templates the current user has hidden from their own view, for the restore screen.</summary>
    [HttpGet("hidden")]
    public async Task<ActionResult<List<FavoriteQuery>>> GetHidden()
    {
        var all = await _favorites.GetAllAsync();
        return Ok(all.Where(f => f.IsTemplate &&
            f.HiddenByUsernames.Any(u => u.Equals(Username, StringComparison.OrdinalIgnoreCase))).ToList());
    }

    /// <summary>Restores a template the current user previously hid.</summary>
    [HttpPost("{id:guid}/unhide")]
    public async Task<IActionResult> Unhide(Guid id)
    {
        var favorite = await _favorites.GetByIdAsync(id);
        if (favorite is null) return NotFound();
        favorite.HiddenByUsernames.RemoveAll(u => u.Equals(Username, StringComparison.OrdinalIgnoreCase));
        await _favorites.SaveAsync(favorite);
        return NoContent();
    }

    [HttpPost("{id:guid}/hide")]
    public async Task<IActionResult> Hide(Guid id)
    {
        var favorite = await _favorites.GetByIdAsync(id);
        if (favorite is null) return NotFound();
        if (favorite.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "You own this template - use delete instead of hide." });

        if (!favorite.HiddenByUsernames.Any(u => u.Equals(Username, StringComparison.OrdinalIgnoreCase)))
            favorite.HiddenByUsernames.Add(Username);

        await _favorites.SaveAsync(favorite);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var favorite = await _favorites.GetByIdAsync(id);
        if (favorite is null) return NotFound();
        if (!string.IsNullOrEmpty(favorite.OwnerUsername)
            && !favorite.OwnerUsername.Equals(Username, StringComparison.OrdinalIgnoreCase)
            && !(favorite.IsTemplate && CanEditTemplates))
            return Forbid();

        var removed = await _favorites.DeleteAsync(id);
        return removed ? NoContent() : NotFound();
    }
}
