using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Authorize]
public class ErrorLogController : ControllerBase
{
    private readonly IErrorLogRepository _errorLog;

    public ErrorLogController(IErrorLogRepository errorLog) => _errorLog = errorLog;

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    private static string Trim(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);

    /// <summary>Any signed-in user's browser reports an error it's about to show them, so admins
    /// see what users see. Lengths are capped so a misbehaving client can't flood the log store.</summary>
    [HttpPost("api/error-log/report")]
    public async Task<IActionResult> Report([FromBody] ReportClientErrorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) return NoContent();

        await _errorLog.AddAsync(new ErrorLogEntry
        {
            Username = Username,
            Source = "Client",
            Method = Trim(request.Method, 10),
            Path = Trim(request.Path, 300),
            StatusCode = request.StatusCode,
            Message = Trim(request.Message, 1000)
        });
        return NoContent();
    }

    [HttpGet("api/admin/error-log")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<ErrorLogEntryResponse>>> GetRecent(
        [FromQuery] int limit = 200, [FromQuery] int skip = 0, [FromQuery] string? source = null)
    {
        var entries = await _errorLog.GetRecentAsync(Math.Clamp(limit, 1, 500), skip,
            string.IsNullOrWhiteSpace(source) ? null : source);
        return Ok(entries.Select(e => new ErrorLogEntryResponse
        {
            Id = e.Id, Username = e.Username, Source = e.Source, Method = e.Method, Path = e.Path,
            StatusCode = e.StatusCode, Message = e.Message, Details = e.Details, CreatedAtUtc = e.CreatedAtUtc
        }).ToList());
    }

    [HttpDelete("api/admin/error-log")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Clear()
    {
        await _errorLog.ClearAsync();
        return NoContent();
    }
}
