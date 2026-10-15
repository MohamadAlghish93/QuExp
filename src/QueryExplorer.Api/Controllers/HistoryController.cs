using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/history")]
[Authorize]
public class HistoryController : ControllerBase
{
    private readonly IQueryHistoryRepository _history;

    public HistoryController(IQueryHistoryRepository history) => _history = history;

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<List<HistoryEntryResponse>>> GetRecent([FromQuery] int limit = 50, [FromQuery] int skip = 0)
    {
        limit = Math.Clamp(limit, 1, 200);
        var entries = await _history.GetRecentAsync(Username, limit, skip);
        return Ok(entries.Select(ToResponse).ToList());
    }

    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        await _history.ClearAsync(Username);
        return NoContent();
    }

    private static HistoryEntryResponse ToResponse(Domain.Entities.QueryHistoryEntry e) => new()
    {
        Id = e.Id,
        ConnectionId = e.ConnectionId,
        ConnectionName = e.ConnectionName,
        Sql = e.Sql,
        Parameters = string.IsNullOrEmpty(e.ParametersJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(e.ParametersJson),
        Success = e.Success,
        Error = e.Error,
        RowCount = e.RowCount,
        DurationMs = e.DurationMs,
        ExecutedAtUtc = e.ExecutedAtUtc
    };
}
