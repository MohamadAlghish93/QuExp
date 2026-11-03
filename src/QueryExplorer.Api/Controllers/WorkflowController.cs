using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Workflows;
using QueryExplorer.Domain.Entities;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Api.Controllers;

/// <summary>
/// Reusable multi-step workflows for templates.
///
/// A template in a workflow is a draft (hidden from everyone but its owner, Admins/Editors and the
/// people working it) until it is official. Steps are grouped into STAGES: steps flagged
/// ParallelWithPrevious run at the same time as the step above, and every step of a stage must be
/// passed (by its own responsible group) before the template moves to the next stage.
///
/// A step can require APPROVAL: someone responsible clicks Approve, it is recorded ("approved by"),
/// and the step can't be passed until then. If that step also makes the template official, the
/// approval is what publishes it. Only an Admin can revoke an approval.
///
/// Forward is always one stage at a time; back can jump to any earlier stage (and clears the
/// approvals and progress of everything from there on, so reworked steps are approved again).
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

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool InGroup(IEnumerable<string> groups, string? name) =>
        !string.IsNullOrWhiteSpace(name) && groups.Any(g => Same(g, name));

    private async Task<List<string>> GroupsOfCurrentUser() =>
        (await _users.GetByUsernameAsync(Username))?.Groups ?? new List<string>();

    // ---------------------------------------------------------------- state helpers

    private static bool InWorkflow(FavoriteQuery f) => f.WorkflowSteps.Count > 0;
    private static List<List<int>> StagesOf(FavoriteQuery f) => WorkflowStages.Build(f.WorkflowSteps);

    private static List<int> CurrentStage(FavoriteQuery f)
    {
        var stages = StagesOf(f);
        return stages[Math.Clamp(f.WorkflowStepIndex, 0, stages.Count - 1)];
    }

    private static bool IsApproved(FavoriteQuery f, int step) => f.WorkflowApprovals.Any(a => a.StepIndex == step);

    /// <summary>
    /// Official = every step of the current stage that makes it official is satisfied: reaching it is
    /// enough, unless it requires approval, in which case it must have been approved.
    /// </summary>
    private static bool IsOfficial(FavoriteQuery f)
    {
        var makers = CurrentStage(f).Where(i => f.WorkflowSteps[i].MakesOfficial).ToList();
        return makers.Count > 0 && makers.All(i => !f.WorkflowSteps[i].RequiresApproval || IsApproved(f, i));
    }

    /// <summary>Nothing left to do: it's on the last stage and official.</summary>
    private static bool IsFinished(FavoriteQuery f) => f.WorkflowStepIndex >= StagesOf(f).Count - 1 && IsOfficial(f);

    private static void SyncApproval(FavoriteQuery f) =>
        f.ApprovalStatus = IsOfficial(f) ? TemplateApprovalStatus.Approved : TemplateApprovalStatus.Pending;

    /// <summary>Admin, or a member of the step's responsible group (empty group = the template's owner).</summary>
    private bool Responsible(FavoriteQuery f, int step, List<string> groups)
    {
        if (IsAdmin) return true;
        var group = f.WorkflowSteps[step].ResponsibleGroup;
        return string.IsNullOrWhiteSpace(group) ? Same(f.OwnerUsername, Username) : InGroup(groups, group);
    }

    private List<int> ActionableSteps(FavoriteQuery f, List<string> groups) =>
        CurrentStage(f).Where(i => !f.WorkflowPassedSteps.Contains(i) && Responsible(f, i, groups)).ToList();

    private bool CanActInStage(FavoriteQuery f, List<string> groups) =>
        CurrentStage(f).Any(i => Responsible(f, i, groups));

    /// <summary>Owner, Admin, or in a group responsible for ANY step - they need to see its history.</summary>
    private bool IsParticipant(FavoriteQuery f, List<string> groups) =>
        IsAdmin || Same(f.OwnerUsername, Username) || f.WorkflowSteps.Any(s => InGroup(groups, s.ResponsibleGroup));

    private string StageName(FavoriteQuery f, int stage)
    {
        var stages = StagesOf(f);
        return WorkflowStages.Name(f.WorkflowSteps, stages[Math.Clamp(stage, 0, stages.Count - 1)]);
    }

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
            Steps = request.Steps.Select((s, i) => new WorkflowStep
            {
                Name = s.Name.Trim(),
                ResponsibleGroup = (s.ResponsibleGroup ?? "").Trim(),
                MakesOfficial = s.MakesOfficial,
                RequiresApproval = s.RequiresApproval,
                ParallelWithPrevious = i > 0 && s.ParallelWithPrevious // the first step has nothing to run alongside
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

    // ---------------------------------------------------------------- acting on a template

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
        {
            Name = s.Name, ResponsibleGroup = s.ResponsibleGroup, MakesOfficial = s.MakesOfficial,
            RequiresApproval = s.RequiresApproval, ParallelWithPrevious = s.ParallelWithPrevious
        }).ToList();
        favorite.WorkflowStepIndex = 0;
        favorite.WorkflowPassedSteps = new List<int>();
        favorite.WorkflowApprovals = new List<WorkflowApproval>();
        SyncApproval(favorite); // enters as a draft unless the first stage itself makes it official
        await _favorites.SaveAsync(favorite);

        var first = StageName(favorite, 0);
        await AddHistory(favorite, "Started", 0, 0, first, first, request.Note);
        return NoContent();
    }

    /// <summary>Forward (one stage) for a single-step stage, or back to any earlier stage.</summary>
    [HttpPost("templates/{favoriteId:guid}/move")]
    public async Task<IActionResult> Move(Guid favoriteId, [FromBody] MoveWorkflowRequest request)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !InWorkflow(favorite)) return NotFound(new { error = "This template isn't in a workflow." });

        var groups = await GroupsOfCurrentUser();
        var stages = StagesOf(favorite);
        var from = favorite.WorkflowStepIndex;
        var to = request.ToStepIndex;

        if (to < 0 || to >= stages.Count) return BadRequest(new { error = "That step doesn't exist." });
        if (to == from) return BadRequest(new { error = "It's already at that step." });

        if (to > from)
        {
            if (to > from + 1) return BadRequest(new { error = "Steps can't be skipped going forward - move to the next step, or jump back to an earlier one." });
            var stage = stages[from];
            if (stage.Count > 1)
                return BadRequest(new { error = "This stage has parallel steps - each responsible group passes its own step, and it moves on once all are passed." });
            return await PassStep(favorite, stage[0], request.Note, groups);
        }

        // Back
        if (!CanActInStage(favorite, groups))
            return StatusCode(403, new { error = $"Only the group(s) responsible for \"{StageName(favorite, from)}\" (or an Admin) can send it back from here." });

        var wasOfficial = IsOfficial(favorite);
        var fromName = StageName(favorite, from);
        // Everything from the target stage onwards is reworked, so its sign-offs and progress no longer stand.
        favorite.WorkflowApprovals.RemoveAll(a => WorkflowStages.StageOf(stages, a.StepIndex) >= to);
        favorite.WorkflowPassedSteps = new List<int>();
        favorite.WorkflowStepIndex = to;
        await SaveAsync(favorite, wasOfficial, $"{fromName} -> {StageName(favorite, to)}");

        await AddHistory(favorite, "Back", from, to, fromName, StageName(favorite, to), request.Note);
        await NotifyAsync(favorite, UsersInStage(favorite, to, await _users.GetAllAsync()),
            $"@{Username} sent \"{favorite.Name}\" back to \"{StageName(favorite, to)}\" (from \"{fromName}\")", request.Note);
        return NoContent();
    }

    /// <summary>Passes one step of the current stage. When every step of the stage is passed, it moves on by itself.</summary>
    [HttpPost("templates/{favoriteId:guid}/pass")]
    public async Task<IActionResult> Pass(Guid favoriteId, [FromBody] PassWorkflowRequest request)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !InWorkflow(favorite)) return NotFound(new { error = "This template isn't in a workflow." });
        return await PassStep(favorite, request.StepIndex, request.Note, await GroupsOfCurrentUser());
    }

    private async Task<IActionResult> PassStep(FavoriteQuery favorite, int stepIndex, string? note, List<string> groups)
    {
        var stage = CurrentStage(favorite);
        if (!stage.Contains(stepIndex)) return BadRequest(new { error = "That step isn't active right now." });
        if (favorite.WorkflowPassedSteps.Contains(stepIndex)) return BadRequest(new { error = "That step is already passed." });

        var step = favorite.WorkflowSteps[stepIndex];
        if (!Responsible(favorite, stepIndex, groups))
        {
            var who = string.IsNullOrWhiteSpace(step.ResponsibleGroup) ? "the template's owner" : $"the \"{step.ResponsibleGroup}\" group";
            return StatusCode(403, new { error = $"Only {who} (or an Admin) can pass \"{step.Name}\"." });
        }
        if (step.RequiresApproval && !IsApproved(favorite, stepIndex))
            return BadRequest(new { error = $"\"{step.Name}\" needs approval first - click Approve, then pass it on." });

        var wasOfficial = IsOfficial(favorite);
        var stageIndex = favorite.WorkflowStepIndex;
        var stageName = StageName(favorite, stageIndex);
        favorite.WorkflowPassedSteps.Add(stepIndex);

        var allPassed = stage.All(favorite.WorkflowPassedSteps.Contains);
        var hasNext = stageIndex < StagesOf(favorite).Count - 1;
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // Only parallel stages need a separate "passed" line - for a single step the Forward entry says it all.
        if (stage.Count > 1)
            await AddHistory(favorite, "Passed", stageIndex, stageIndex, step.Name, step.Name, cleanNote);

        if (allPassed && hasNext)
        {
            favorite.WorkflowStepIndex = stageIndex + 1;
            favorite.WorkflowPassedSteps = new List<int>();
            await SaveAsync(favorite, wasOfficial, $"{stageName} -> {StageName(favorite, stageIndex + 1)}");

            await AddHistory(favorite, "Forward", stageIndex, stageIndex + 1, stageName, StageName(favorite, stageIndex + 1),
                stage.Count > 1 ? null : cleanNote);
            var next = StageName(favorite, stageIndex + 1);
            await NotifyAsync(favorite, UsersInStage(favorite, stageIndex + 1, await _users.GetAllAsync()),
                IsOfficial(favorite) ? $"\"{favorite.Name}\" is now official (\"{next}\")"
                                     : $"@{Username} moved \"{favorite.Name}\" to \"{next}\" - it needs your group's attention", cleanNote);
        }
        else
        {
            await SaveAsync(favorite, wasOfficial, $"{step.Name} passed");
            if (!allPassed)
            {
                var waiting = stage.Where(i => !favorite.WorkflowPassedSteps.Contains(i)).Select(i => favorite.WorkflowSteps[i].Name);
                await NotifyAsync(favorite, new[] { favorite.OwnerUsername },
                    $"@{Username} passed \"{step.Name}\" on \"{favorite.Name}\" - still waiting on: {string.Join(", ", waiting)}", cleanNote);
            }
        }
        return NoContent();
    }

    /// <summary>Approves a step. The approval is recorded ("approved by"), lets the step be passed, and - if the
    /// step makes the template official - publishes it.</summary>
    [HttpPost("templates/{favoriteId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid favoriteId, [FromBody] ApproveWorkflowRequest request)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !InWorkflow(favorite)) return NotFound(new { error = "This template isn't in a workflow." });

        var stage = CurrentStage(favorite);
        var stepIndex = request.StepIndex ?? (stage.Count == 1 ? stage[0] : -1);
        if (stepIndex < 0) return BadRequest(new { error = "Say which step you're approving." });
        if (!stage.Contains(stepIndex)) return BadRequest(new { error = "That step isn't active right now." });

        var step = favorite.WorkflowSteps[stepIndex];
        if (!step.RequiresApproval) return BadRequest(new { error = $"\"{step.Name}\" doesn't use approval." });
        if (IsApproved(favorite, stepIndex)) return BadRequest(new { error = "That step is already approved." });

        var groups = await GroupsOfCurrentUser();
        if (!Responsible(favorite, stepIndex, groups))
        {
            var who = string.IsNullOrWhiteSpace(step.ResponsibleGroup) ? "the template's owner" : $"the \"{step.ResponsibleGroup}\" group";
            return StatusCode(403, new { error = $"Only {who} (or an Admin) can approve \"{step.Name}\"." });
        }

        var wasOfficial = IsOfficial(favorite);
        favorite.WorkflowApprovals.Add(new WorkflowApproval
        {
            StepIndex = stepIndex, StepName = step.Name, ApprovedByUsername = Username, ApprovedAtUtc = DateTime.UtcNow
        });
        await SaveAsync(favorite, wasOfficial, $"{step.Name} approved");

        var stageIndex = favorite.WorkflowStepIndex;
        await AddHistory(favorite, "Approved", stageIndex, stageIndex, step.Name, step.Name, request.Note);
        await NotifyAsync(favorite, new[] { favorite.OwnerUsername },
            IsOfficial(favorite) && !wasOfficial
                ? $"@{Username} approved \"{favorite.Name}\" - it is now official"
                : $"@{Username} approved \"{step.Name}\" on \"{favorite.Name}\"", request.Note);
        return NoContent();
    }

    /// <summary>Admin only. Withdraws an approval on the current stage; if that was what made the template official it goes back to draft.</summary>
    [HttpPost("templates/{favoriteId:guid}/revoke")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Revoke(Guid favoriteId, [FromBody] RevokeWorkflowRequest request)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !InWorkflow(favorite)) return NotFound(new { error = "This template isn't in a workflow." });

        var approval = favorite.WorkflowApprovals.FirstOrDefault(a => a.StepIndex == request.StepIndex);
        if (approval is null) return BadRequest(new { error = "That step has no approval to revoke." });
        if (!CurrentStage(favorite).Contains(request.StepIndex))
            return BadRequest(new { error = "Only approvals on the template's current step can be revoked - send it back to that step instead." });

        var wasOfficial = IsOfficial(favorite);
        favorite.WorkflowApprovals.Remove(approval);
        favorite.WorkflowPassedSteps.Remove(request.StepIndex); // approving it is what let it be passed
        await SaveAsync(favorite, wasOfficial, $"{approval.StepName} approval revoked");

        var stageIndex = favorite.WorkflowStepIndex;
        await AddHistory(favorite, "Revoked", stageIndex, stageIndex, approval.StepName, approval.StepName, request.Note);
        await _audit.AddAsync(new AuditLogEntry
        {
            ActorUsername = Username, Action = "TemplateApprovalRevoked",
            TargetDescription = $"{favorite.Name}: {approval.StepName} (approved by {approval.ApprovedByUsername})"
        });
        await NotifyAsync(favorite, new[] { favorite.OwnerUsername, approval.ApprovedByUsername },
            $"@{Username} revoked the approval of \"{approval.StepName}\" on \"{favorite.Name}\"", request.Note);
        return NoContent();
    }

    /// <summary>Takes a template out of its workflow (e.g. wrong one was chosen). It goes back to plain pending approval.</summary>
    [HttpDelete("templates/{favoriteId:guid}")]
    public async Task<IActionResult> Detach(Guid favoriteId)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null || !InWorkflow(favorite)) return NotFound();
        if (!Same(favorite.OwnerUsername, Username) && !IsAdmin)
            return StatusCode(403, new { error = "Only the template's owner or an Admin can detach its workflow." });

        var index = favorite.WorkflowStepIndex;
        var name = StageName(favorite, index);
        favorite.WorkflowDefinitionId = null;
        favorite.WorkflowName = null;
        favorite.WorkflowSteps = new List<WorkflowStep>();
        favorite.WorkflowStepIndex = 0;
        favorite.WorkflowPassedSteps = new List<int>();
        favorite.WorkflowApprovals = new List<WorkflowApproval>();
        favorite.ApprovalStatus = TemplateApprovalStatus.Pending;
        await _favorites.SaveAsync(favorite);

        await AddHistory(favorite, "Detached", index, index, name, name, null);
        return NoContent();
    }

    // ---------------------------------------------------------------- reading

    [HttpGet("templates/{favoriteId:guid}/history")]
    public async Task<IActionResult> History(Guid favoriteId)
    {
        var favorite = await _favorites.GetByIdAsync(favoriteId);
        if (favorite is null) return NotFound();
        if (!IsParticipant(favorite, await GroupsOfCurrentUser())) return Forbid();
        return Ok(await _workflows.GetHistoryAsync(favoriteId));
    }

    /// <summary>What needs my attention, plus my own templates (and ones I take part in) that are in a workflow.</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<WorkflowInboxResponse>> Inbox()
    {
        var groups = await GroupsOfCurrentUser();
        var inFlow = (await _favorites.GetAllAsync()).Where(f => f.IsTemplate && InWorkflow(f)).ToList();

        var result = new WorkflowInboxResponse();
        foreach (var f in inFlow.OrderByDescending(x => x.LastUsedAtUtc ?? x.CreatedAtUtc))
        {
            var item = ToItem(f, groups);
            if (!IsFinished(f) && item.CanAct) result.WaitingForMe.Add(item);
            else if (Same(f.OwnerUsername, Username) || (IsParticipant(f, groups) && !IsAdmin)) result.Other.Add(item);
        }
        return Ok(result);
    }

    // ---------------------------------------------------------------- helpers

    private async Task SaveAsync(FavoriteQuery favorite, bool wasOfficial, string detail)
    {
        SyncApproval(favorite);
        await _favorites.SaveAsync(favorite);

        var nowOfficial = IsOfficial(favorite);
        if (nowOfficial != wasOfficial)
            await _audit.AddAsync(new AuditLogEntry
            {
                ActorUsername = Username,
                Action = nowOfficial ? "TemplateMadeOfficial" : "TemplateReturnedToDraft",
                TargetDescription = $"{favorite.Name} ({detail})"
            });
    }

    private Task AddHistory(FavoriteQuery favorite, string action, int from, int to, string fromName, string toName, string? note) =>
        _workflows.AddHistoryAsync(new WorkflowHistoryEntry
        {
            FavoriteId = favorite.Id, Action = action, FromStepIndex = from, ToStepIndex = to,
            FromStepName = fromName, ToStepName = toName, MovedByUsername = Username,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        });

    /// <summary>Everyone responsible for any step of the stage (the owner stands in for an empty group), plus the owner.</summary>
    private static IEnumerable<string> UsersInStage(FavoriteQuery f, int stage, IEnumerable<User> allUsers)
    {
        var stages = StagesOf(f);
        var who = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { f.OwnerUsername };
        foreach (var step in stages[Math.Clamp(stage, 0, stages.Count - 1)].Select(i => f.WorkflowSteps[i]))
        {
            if (string.IsNullOrWhiteSpace(step.ResponsibleGroup)) continue;
            foreach (var u in allUsers)
                if (InGroup(u.Groups, step.ResponsibleGroup)) who.Add(u.Username);
        }
        return who;
    }

    private async Task NotifyAsync(FavoriteQuery favorite, IEnumerable<string> usernames, string message, string? note)
    {
        if (!string.IsNullOrWhiteSpace(note)) message += $": {note.Trim()}";
        foreach (var username in usernames.Distinct(StringComparer.OrdinalIgnoreCase).Where(u => !Same(u, Username))) // nobody needs telling about their own action
        {
            await _notifications.AddAsync(new AlertNotification
            {
                Username = username, AlertId = favorite.Id, AlertName = favorite.Name, Message = message, RowCount = 0
            });
            await _push.SendToUserAsync(username, "Template workflow", message, "/workflow");
        }
    }

    private WorkflowItemResponse ToItem(FavoriteQuery f, List<string> groups)
    {
        var stage = CurrentStage(f);
        var actionable = ActionableSteps(f, groups);
        var approvable = stage.Where(i => f.WorkflowSteps[i].RequiresApproval && !IsApproved(f, i) && Responsible(f, i, groups)).ToList();
        return new WorkflowItemResponse
        {
            FavoriteId = f.Id, Name = f.Name, Description = f.Description, Sql = f.Sql,
            OwnerUsername = f.OwnerUsername, WorkflowName = f.WorkflowName ?? "",
            Steps = f.WorkflowSteps, CurrentStageIndex = f.WorkflowStepIndex,
            PassedSteps = f.WorkflowPassedSteps, Approvals = f.WorkflowApprovals,
            IsOfficial = IsOfficial(f),
            ActionableSteps = actionable, ApprovableSteps = approvable,
            CanAct = actionable.Count > 0 || approvable.Count > 0,
            CanGoBack = f.WorkflowStepIndex > 0 && CanActInStage(f, groups),
            CanRevoke = IsAdmin && stage.Any(i => IsApproved(f, i))
        };
    }

    private static WorkflowDefinitionResponse ToResponse(WorkflowDefinition d) =>
        new() { Id = d.Id, Name = d.Name, OwnerUsername = d.OwnerUsername, Steps = d.Steps };
}
