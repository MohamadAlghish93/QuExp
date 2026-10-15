using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Api.Controllers;

/// <summary>
/// Reusable multi-step workflows for templates. A template in a workflow is a Draft (hidden from
/// everyone but its owner, Admins/Editors and the people working it) until it reaches a step marked
/// MakesOfficial. It moves FORWARD one step at a time, or BACK to any earlier step (so it can loop
/// through revision). Only the group responsible for its current step (or an Admin) can move it.
/// </summary>
[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowController : ControllerBase
{
    private readonly IWorkflowRepository _workflows;
    private readonly IFavoriteQueryRepository _favorites;
    private readonly IUserRepository _users;
    private readonly IAlertNotificationRepository _notifications;
    private readonly IPushNotificationSender _push;
    private readonly IAuditLogRepository _audit;

    public WorkflowController(
        IWorkflowRepository workflows, IFavoriteQueryRepository favorites, IUserRepository users,
        IAlertNotificationRepository notifications, IPushNotificationSender push, IAuditLogRepository audit)
    {
        _workflows = workflows; _favorites = favorites; _users = users;
        _notifications = notifications; _push = push; _audit = audit;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";
    private bool IsAdmin => Role == "Admin";

    private static bool InWorkflow(FavoriteQuery f) => f.WorkflowSteps.Count > 0;

    private static bool IsTerminalOfficial(FavoriteQuery f) =>
        InWorkflow(f) && f.WorkflowStepIndex == f.WorkflowSteps.Count - 1 && f.WorkflowSteps[f.WorkflowStepIndex].MakesOfficial;

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Admin, or a member of the CURRENT step's responsible group (empty group = the template's owner).</summary>
    private bool CanAct(FavoriteQuery f, string? userGroup)
    {
        if (!InWorkflow(f)) return false;
        if (IsAdmin) return true;
        var step = f.WorkflowSteps[f.WorkflowStepIndex];
        return string.IsNullOrWhiteSpace(step.ResponsibleGroup)
            ? Same(f.OwnerUsername, Username)
            : Same(step.ResponsibleGroup, userGroup);
    }

    /// <summary>Anyone who owns it or belongs to a group responsible for ANY of its steps - they need to see its history.</summary>
    private bool IsParticipant(FavoriteQuery f, string? userGroup) =>
        IsAdmin || Same(f.OwnerUsername, Username) ||
        (!string.IsNullOrWhiteSpace(userGroup) && f.WorkflowSteps.Any(s => Same(s.ResponsibleGroup, userGroup)));

    /// <summary>A template is Official (visible per its sharing settings) only at a MakesOfficial step.</summary>
    private static void SyncApproval(FavoriteQuery f) =>
        f.ApprovalStatus = f.WorkflowSteps[f.WorkflowStepIndex].MakesOfficial
            ? TemplateApprovalStatus.Approved : TemplateApprovalStatus.Pending;

    private async Task<string?> GroupOfCurrentUser() => (await _users.GetByUsernameAsync(Username))?.GroupName;

    // ---------------------------------------------------------------- definitions

    [HttpGet("groups")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<ActionResult<List<string>>> GetGroups() => Ok(await _users.GetDistinctGroupNamesAsync());

    [HttpGet("definitions")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<ActionResult<List<WorkflowDefinitionResponse>>> GetDefinitions()
        => Ok((await _workflows.GetDefinitionsAsync()).Select(ToResponse).ToList());

    [HttpPost("definitions")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<ActionResult<WorkflowDefinitionResponse>> SaveDefinition([FromBody] SaveWorkflowDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Give the workflow a name." });
        if (request.Steps.Count < 2) return BadRequest(new { error = "A workflow needs at least two steps." });
        if (request.Steps.Any(s => string.IsNullOrWhiteSpace(s.Name))) return BadRequest(new { error = "Every step needs a name." });
        if (!request.Steps.Any(s => s.MakesOfficial))
            return BadRequest(new { error = "At least one step must make the template official, otherwise it can never be published." });

        // A step whose group nobody belongs to would strand templates there forever - fail early.
        var groups = await _users.GetDistinctGroupNamesAsync();
        var unknown = request.Steps.Select(s => s.ResponsibleGroup?.Trim())
            .Where(g => !string.IsNullOrEmpty(g) && !groups.Any(x => Same(x, g))).Distinct().ToList();
        if (unknown.Count > 0)
            return BadRequest(new { error = $"No users are in the group(s): {string.Join(", ", unknown)}. Assign users to the group on the Users page first, or leave the step's group empty to make the template's owner responsible." });

        var existing = request.Id.HasValue ? await _workflows.GetDefinitionAsync(request.Id.Value) : null;
        if (existing is not null && !Same(existing.OwnerUsername, Username) && !IsAdmin) return Forbid();

        var saved = await _workflows.SaveDefinitionAsync(new WorkflowDefinition
        {
            Id = existing?.Id ?? request.Id ?? Guid.NewGuid(),
            Name = request.Name.Trim(),
            OwnerUsername = existing?.OwnerUsername ?? Username,
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow,
            Steps = request.Steps.Select(s => new WorkflowStep
            {
                Name = s.Name.Trim(), ResponsibleGroup = (s.ResponsibleGroup ?? "").Trim(), MakesOfficial = s.MakesOfficial
            }).ToList()
        });
        return Ok(ToResponse(saved));
    }

    /// <summary>Templates already in this workflow keep their own copy of its steps, so deleting/editing it never disturbs them.</summary>
    [HttpDelete("definitions/{id:guid}")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<IActionResult> DeleteDefinition(Guid id)
    {
        var existing = await _workflows.GetDefinitionAsync(id);
        if (existing is null) return NotFound();
        if (!Same(existing.OwnerUsername, Username) && !IsAdmin) return Forbid();
        await _workflows.DeleteDefinitionAsync(id);
        return NoContent();
    }

    // ---------------------------------------------------------------- moving a template

    [HttpPost("templates/{favoriteId:guid}/start")]
    public async Task<IActionResult> Start(Guid favoriteId, [FromBody] StartWorkflowRequest request)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !favorite.IsTemplate) return NotFound(new { error = "Template not found." });
        if (!Same(favorite.OwnerUsername, Username) && !IsAdmin)
            return StatusCode(403, new { error = "Only the template's owner or an Admin can start its workflow." });
        if (InWorkflow(favorite))
            return BadRequest(new { error = "This template is already in a workflow. Detach it first to start a different one." });

        var definition = await _workflows.GetDefinitionAsync(request.DefinitionId);
        if (definition is null) return BadRequest(new { error = "Workflow not found." });

        favorite.WorkflowDefinitionId = definition.Id;
        favorite.WorkflowName = definition.Name;
        favorite.WorkflowSteps = definition.Steps.Select(s => new WorkflowStep
            { Name = s.Name, ResponsibleGroup = s.ResponsibleGroup, MakesOfficial = s.MakesOfficial }).ToList();
        favorite.WorkflowStepIndex = 0;
        SyncApproval(favorite); // enters as Draft unless step 1 itself makes it official
        await _favorites.SaveAsync(favorite);

        var first = favorite.WorkflowSteps[0].Name;
        await _workflows.AddHistoryAsync(new WorkflowHistoryEntry
        {
            FavoriteId = favorite.Id, Action = "Started", FromStepIndex = 0, ToStepIndex = 0,
            FromStepName = first, ToStepName = first, MovedByUsername = Username, Note = request.Note
        });
        return NoContent();
    }

    [HttpPost("templates/{favoriteId:guid}/move")]
    public async Task<IActionResult> Move(Guid favoriteId, [FromBody] MoveWorkflowRequest request)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !InWorkflow(favorite)) return NotFound(new { error = "This template isn't in a workflow." });

        var userGroup = await GroupOfCurrentUser();
        if (!CanAct(favorite, userGroup))
        {
            var step = favorite.WorkflowSteps[favorite.WorkflowStepIndex];
            var who = string.IsNullOrWhiteSpace(step.ResponsibleGroup) ? "the template's owner" : $"the \"{step.ResponsibleGroup}\" group";
            return StatusCode(403, new { error = $"Only {who} (or an Admin) can move it from \"{step.Name}\"." });
        }

        var from = favorite.WorkflowStepIndex;
        var to = request.ToStepIndex;
        if (to < 0 || to >= favorite.WorkflowSteps.Count) return BadRequest(new { error = "That step doesn't exist." });
        if (to == from) return BadRequest(new { error = "It's already at that step." });
        if (to > from + 1) return BadRequest(new { error = "Steps can't be skipped going forward - move to the next step, or jump back to an earlier one." });

        var action = to > from ? "Forward" : "Back";
        var wasOfficial = favorite.WorkflowSteps[from].MakesOfficial;
        favorite.WorkflowStepIndex = to;
        SyncApproval(favorite);
        await _favorites.SaveAsync(favorite);

        var fromName = favorite.WorkflowSteps[from].Name;
        var toStep = favorite.WorkflowSteps[to];
        await _workflows.AddHistoryAsync(new WorkflowHistoryEntry
        {
            FavoriteId = favorite.Id, Action = action, FromStepIndex = from, ToStepIndex = to,
            FromStepName = fromName, ToStepName = toStep.Name, MovedByUsername = Username,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        });

        if (toStep.MakesOfficial != wasOfficial)
            await _audit.AddAsync(new AuditLogEntry
            {
                ActorUsername = Username,
                Action = toStep.MakesOfficial ? "TemplateMadeOfficial" : "TemplateReturnedToDraft",
                TargetDescription = $"{favorite.Name} ({fromName} -> {toStep.Name})"
            });

        await NotifyAsync(favorite, toStep, action, fromName, request.Note);
        return NoContent();
    }

    /// <summary>Takes a template out of its workflow (e.g. wrong one was chosen). It goes back to plain Pending approval.</summary>
    [HttpDelete("templates/{favoriteId:guid}")]
    public async Task<IActionResult> Detach(Guid favoriteId)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !InWorkflow(favorite)) return NotFound();
        if (!Same(favorite.OwnerUsername, Username) && !IsAdmin)
            return StatusCode(403, new { error = "Only the template's owner or an Admin can detach its workflow." });

        var name = favorite.WorkflowSteps[favorite.WorkflowStepIndex].Name;
        var index = favorite.WorkflowStepIndex;
        favorite.WorkflowDefinitionId = null;
        favorite.WorkflowName = null;
        favorite.WorkflowSteps = new List<WorkflowStep>();
        favorite.WorkflowStepIndex = 0;
        favorite.ApprovalStatus = TemplateApprovalStatus.Pending;
        await _favorites.SaveAsync(favorite);

        await _workflows.AddHistoryAsync(new WorkflowHistoryEntry
        {
            FavoriteId = favorite.Id, Action = "Detached", FromStepIndex = index, ToStepIndex = index,
            FromStepName = name, ToStepName = name, MovedByUsername = Username
        });
        return NoContent();
    }

    // ---------------------------------------------------------------- reading

    [HttpGet("templates/{favoriteId:guid}/history")]
    public async Task<IActionResult> History(Guid favoriteId)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null) return NotFound();
        var group = await GroupOfCurrentUser();
        // A template that has since been detached has no steps left to match against - only owner/Admin then.
        if (!IsParticipant(favorite, group)) return Forbid();
        return Ok(await _workflows.GetHistoryAsync(favoriteId));
    }

    /// <summary>What needs my attention, plus my own templates that are in a workflow.</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<WorkflowInboxResponse>> Inbox()
    {
        var group = await GroupOfCurrentUser();
        var inFlow = (await _favorites.GetAllAsync()).Where(f => f.IsTemplate && InWorkflow(f)).ToList();

        var result = new WorkflowInboxResponse();
        foreach (var f in inFlow.OrderByDescending(x => x.LastUsedAtUtc ?? x.CreatedAtUtc))
        {
            var canAct = CanAct(f, group);
            var item = ToItem(f, canAct);
            if (canAct && !IsTerminalOfficial(f)) result.WaitingForMe.Add(item);
            else if (Same(f.OwnerUsername, Username) || (IsParticipant(f, group) && !IsAdmin)) result.Other.Add(item);
        }
        return Ok(result);
    }

    // ---------------------------------------------------------------- helpers

    private async Task NotifyAsync(FavoriteQuery favorite, WorkflowStep toStep, string action, string fromName, string? note)
    {
        var who = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(toStep.ResponsibleGroup)) who.Add(favorite.OwnerUsername);
        else
            foreach (var u in await _users.GetAllAsync())
                if (Same(u.GroupName, toStep.ResponsibleGroup)) who.Add(u.Username);

        who.Add(favorite.OwnerUsername); // the owner always hears about progress on their template
        who.Remove(Username);            // ...but nobody needs a notification about their own action

        var message = action == "Back"
            ? $"@{Username} sent \"{favorite.Name}\" back to \"{toStep.Name}\" (from \"{fromName}\")"
            : toStep.MakesOfficial
                ? $"\"{favorite.Name}\" is now official (\"{toStep.Name}\")"
                : $"@{Username} moved \"{favorite.Name}\" to \"{toStep.Name}\" - it needs your group's attention";
        if (!string.IsNullOrWhiteSpace(note)) message += $": {note.Trim()}";

        foreach (var username in who)
        {
            await _notifications.AddAsync(new AlertNotification
            {
                Username = username, AlertId = favorite.Id, AlertName = favorite.Name, Message = message, RowCount = 0
            });
            await _push.SendToUserAsync(username, "Template workflow", message, "/workflow");
        }
    }

    private static WorkflowItemResponse ToItem(FavoriteQuery f, bool canAct) => new()
    {
        FavoriteId = f.Id, Name = f.Name, Description = f.Description, Sql = f.Sql,
        OwnerUsername = f.OwnerUsername, WorkflowName = f.WorkflowName ?? "",
        Steps = f.WorkflowSteps, CurrentStepIndex = f.WorkflowStepIndex,
        IsOfficial = f.WorkflowSteps[f.WorkflowStepIndex].MakesOfficial, CanAct = canAct
    };

    private static WorkflowDefinitionResponse ToResponse(WorkflowDefinition d) =>
        new() { Id = d.Id, Name = d.Name, OwnerUsername = d.OwnerUsername, Steps = d.Steps };
}
