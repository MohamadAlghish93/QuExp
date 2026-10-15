using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IAlertNotificationRepository _notifications;

    public NotificationsController(IAlertNotificationRepository notifications) => _notifications = notifications;

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<List<NotificationResponse>>> GetRecent([FromQuery] int limit = 30)
    {
        var items = await _notifications.GetForUserAsync(Username, Math.Clamp(limit, 1, 100));
        return Ok(items.Select(n => new NotificationResponse
        {
            Id = n.Id,
            AlertId = n.AlertId,
            AlertName = n.AlertName,
            Message = n.Message,
            RowCount = n.RowCount,
            CreatedAtUtc = n.CreatedAtUtc,
            IsRead = n.IsRead
        }).ToList());
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount() => Ok(await _notifications.GetUnreadCountAsync(Username));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        await _notifications.MarkReadAsync(Username, id);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _notifications.MarkAllReadAsync(Username);
        return NoContent();
    }
}
