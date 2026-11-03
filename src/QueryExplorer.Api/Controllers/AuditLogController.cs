using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/admin/audit-log")]
[Authorize(Roles = "Admin")]
public class AuditLogController : ControllerBase
{
    private readonly IAuditLogRepository _auditLog;

    public AuditLogController(IAuditLogRepository auditLog) => _auditLog = auditLog;

    [HttpGet]
    public async Task<ActionResult<List<AuditLogEntryResponse>>> GetRecent([FromQuery] int limit = 100, [FromQuery] int skip = 0)
    {
        var entries = await _auditLog.GetRecentAsync(Math.Clamp(limit, 1, 500), skip);
        return Ok(entries.Select(e => new AuditLogEntryResponse
        {
            Id = e.Id,
            ActorUsername = e.ActorUsername,
            Action = e.Action,
            TargetDescription = e.TargetDescription,
            CreatedAtUtc = e.CreatedAtUtc
        }).ToList());
    }
}
