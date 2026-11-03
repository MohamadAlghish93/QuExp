using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

/// <summary>
/// "Assign a favorite to a teammate" (the @mention-a-saved-query feature). Creating an
/// assignment immediately notifies the assignee; AssignmentReminderService handles the
/// recurring "it's your turn again" reminders in the background.
/// </summary>
[ApiController]
[Route("api/assignments")]
[Authorize]
public class AssignmentsController : ControllerBase
{
    private readonly IAssignmentRepository _assignments;
    private readonly IFavoriteQueryRepository _favorites;
    private readonly IUserRepository _users;
    private readonly IAlertNotificationRepository _notifications;
    private readonly IPushNotificationSender _pushSender;

    public AssignmentsController(
        IAssignmentRepository assignments, IFavoriteQueryRepository favorites, IUserRepository users,
        IAlertNotificationRepository notifications, IPushNotificationSender pushSender)
    {
        _assignments = assignments;
        _favorites = favorites;
        _users = users;
        _notifications = notifications;
        _pushSender = pushSender;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    [HttpGet("assigned-to-me")]
    public async Task<ActionResult<List<AssignmentResponse>>> GetAssignedToMe()
        => Ok((await _assignments.GetAssignedToAsync(Username)).Select(ToResponse).ToList());

    [HttpGet("assigned-by-me")]
    public async Task<ActionResult<List<AssignmentResponse>>> GetAssignedByMe()
        => Ok((await _assignments.GetAssignedByAsync(Username)).Select(ToResponse).ToList());

    [HttpPost]
    public async Task<ActionResult<AssignmentResponse>> Create([FromBody] CreateAssignmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AssignedToUsername))
            return BadRequest(new { error = "Pick a teammate to assign this to." });

        var favorite = await _favorites.GetByIdAsync(request.FavoriteId);
        if (favorite is null) return NotFound(new { error = "Favorite not found." });

        var assignee = await _users.GetByUsernameAsync(request.AssignedToUsername.Trim());
        if (assignee is null) return BadRequest(new { error = "That user doesn't exist." });

        var assignment = new FavoriteAssignment
        {
            FavoriteId = favorite.Id,
            FavoriteName = favorite.Name,
            Sql = favorite.Sql,
            ConnectionId = favorite.ConnectionId,
            AssignedByUsername = Username,
            AssignedToUsername = assignee.Username,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            RepeatIntervalDays = Math.Max(0, request.RepeatIntervalDays)
        };
        // The immediate "assigned to you" notification below covers right now.
        // AssignmentReminderService only needs to pick this up for the NEXT
        // occurrence - if we left NextDueAtUtc at "now", the background poller
        // (every 5 min) would send a duplicate reminder seconds after this request.
        // For a one-time assignment there's no next occurrence, so push NextDueAtUtc
        // far into the future rather than deactivating it - it should still show up
        // in "assigned to you" as an open to-do until the assignee marks it done.
        assignment.NextDueAtUtc = assignment.RepeatIntervalDays > 0
            ? DateTime.UtcNow.AddDays(assignment.RepeatIntervalDays)
            : DateTime.MaxValue;

        var saved = await _assignments.SaveAsync(assignment);

        // Immediate "you've been assigned" notification, separate from the recurring due-reminders
        // AssignmentReminderService generates on NextDueAtUtc.
        var sqlPreview = favorite.Sql.Length > 80 ? favorite.Sql[..80].Trim() + "…" : favorite.Sql.Trim();
        var message = $"{Username} assigned you \"{favorite.Name}\" to check" +
                      (assignment.RepeatIntervalDays > 0 ? $" every {assignment.RepeatIntervalDays} day(s)" : "") +
                      (assignment.Note is null ? "" : $": {assignment.Note}") +
                      $" — {sqlPreview}";

        await _notifications.AddAsync(new AlertNotification
        {
            Username = assignee.Username,
            AlertId = saved.Id,
            AlertName = favorite.Name,
            Message = message,
            RowCount = 0
        });
        await _pushSender.SendToUserAsync(assignee.Username, "New query assigned to you", message, "/favorites");

        return Ok(ToResponse(saved));
    }

    /// <summary>Marks the current due cycle complete, logs when, notifies the person who assigned it,
    /// and (if recurring) schedules the next cycle from now.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var assignment = await _assignments.GetByIdAsync(id);
        if (assignment is null || !assignment.AssignedToUsername.Equals(Username, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var completedAt = DateTime.UtcNow;
        assignment.LastCompletedAtUtc = completedAt;
        assignment.CompletionHistory.Add(completedAt);

        if (assignment.RepeatIntervalDays > 0)
            assignment.NextDueAtUtc = completedAt.AddDays(assignment.RepeatIntervalDays);
        else
            assignment.Active = false;

        await _assignments.SaveAsync(assignment);

        // Previously "mark done" was silent - the assigner had no way to know it happened.
        // Notify them, same as any other in-app notification.
        var message = $"@{Username} marked \"{assignment.FavoriteName}\" done ({completedAt:g} UTC).";
        await _notifications.AddAsync(new AlertNotification
        {
            Username = assignment.AssignedByUsername,
            AlertId = assignment.Id,
            AlertName = assignment.FavoriteName,
            Message = message,
            RowCount = 0
        });
        await _pushSender.SendToUserAsync(assignment.AssignedByUsername, "Assignment completed", message, "/favorites");

        return Ok(new AssignmentResponse
        {
            Id = assignment.Id,
            LastCompletedAtUtc = assignment.LastCompletedAtUtc,
            CompletionHistory = assignment.CompletionHistory,
            NextDueAtUtc = assignment.NextDueAtUtc,
            Active = assignment.Active
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var removed = await _assignments.DeleteAsync(id, Username);
        return removed ? NoContent() : NotFound();
    }

    private static AssignmentResponse ToResponse(FavoriteAssignment a) => new()
    {
        Id = a.Id,
        FavoriteId = a.FavoriteId,
        FavoriteName = a.FavoriteName,
        Sql = a.Sql,
        ConnectionId = a.ConnectionId,
        AssignedByUsername = a.AssignedByUsername,
        AssignedToUsername = a.AssignedToUsername,
        Note = a.Note,
        RepeatIntervalDays = a.RepeatIntervalDays,
        NextDueAtUtc = a.NextDueAtUtc,
        LastCompletedAtUtc = a.LastCompletedAtUtc,
        CompletionHistory = a.CompletionHistory,
        Active = a.Active
    };
}
