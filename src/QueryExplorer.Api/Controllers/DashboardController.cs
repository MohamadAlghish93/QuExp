using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Validation;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardRepository _dashboard;
    private readonly IConnectionRepository _connections;
    private readonly IQueryProviderFactory _providerFactory;
    private readonly IConnectionStringResolver _resolver;

    public DashboardController(
        IDashboardRepository dashboard, IConnectionRepository connections,
        IQueryProviderFactory providerFactory, IConnectionStringResolver resolver)
    {
        _dashboard = dashboard;
        _connections = connections;
        _providerFactory = providerFactory;
        _resolver = resolver;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";

    /// <summary>
    /// Returns the user's pinned items WITH fresh data - each item's group-by is
    /// re-run against the live database rather than serving a stale snapshot.
    /// A failure on one item doesn't take down the rest of the dashboard.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DashboardItemResponse>>> GetDashboard(CancellationToken ct)
    {
        var items = await _dashboard.GetForUserAsync(Username);
        var connections = await _connections.GetAllAsync();
        var results = new List<DashboardItemResponse>();

        foreach (var item in items)
        {
            var connection = connections.FirstOrDefault(c => c.Id == item.ConnectionId);
            var response = new DashboardItemResponse
            {
                Id = item.Id,
                Title = item.Title,
                ConnectionId = item.ConnectionId,
                ConnectionName = connection?.Name ?? "(connection removed)",
                Sql = item.Sql,
                Parameters = item.Parameters,
                GroupByColumn = item.GroupByColumn,
                OwnerUsername = item.Username,
                IsOwnedByMe = item.Username.Equals(Username, StringComparison.OrdinalIgnoreCase),
                SharedWithUsernames = item.SharedWithUsernames
            };

            if (connection is null)
            {
                response.Error = "The connection this item used has been removed.";
            }
            else if (!connection.CanAccess(Username, Role))
            {
                response.Error = "You no longer have access to this item's connection.";
            }
            else
            {
                try
                {
                    var provider = _providerFactory.GetProvider(connection.ProviderType);
                    var connectionString = _resolver.Resolve(connection);
                    var parameters = item.Parameters.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                    var groups = await provider.ExecuteGroupByAsync(connectionString, item.Sql, item.GroupByColumn, 10, parameters, ct);
                    response.Groups = groups.Select(g => new GroupByRow { Value = g.Value, Count = g.Count }).ToList();
                }
                catch (Exception ex)
                {
                    response.Error = ex.Message;
                }
            }

            results.Add(response);
        }

        return Ok(results);
    }

    [HttpPost]
    public async Task<ActionResult<DashboardItemResponse>> Pin([FromBody] SaveDashboardItemRequest request)
    {
        var (isValid, error) = SqlValidator.Validate(request.Sql);
        if (!isValid) return BadRequest(new { error });
        if (!IdentifierValidator.IsSafe(request.GroupByColumn))
            return BadRequest(new { error = "Invalid column name." });

        var targetConnection = await _connections.GetByIdAsync(request.ConnectionId);
        if (targetConnection is null) return BadRequest(new { error = "Unknown connection id." });
        if (!targetConnection.CanAccess(Username, Role)) return Forbid();

        var item = new DashboardItem
        {
            Username = Username,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled" : request.Title.Trim(),
            ConnectionId = request.ConnectionId,
            Sql = request.Sql,
            Parameters = request.Parameters ?? new Dictionary<string, string>(),
            GroupByColumn = request.GroupByColumn
        };

        var saved = await _dashboard.SaveAsync(item);
        return Ok(new { saved.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Unpin(Guid id)
    {
        var removed = await _dashboard.DeleteAsync(Username, id);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>Shares this tile so it also appears on another user's dashboard. Only the owner can share it.</summary>
    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> Share(Guid id, [FromBody] ShareDashboardItemRequest request)
    {
        var item = await _dashboard.GetByIdAsync(id);
        if (item is null) return NotFound();
        if (!item.Username.Equals(Username, StringComparison.OrdinalIgnoreCase))
            return Forbid(); // only the owner decides who else sees their tile

        var target = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            return BadRequest(new { error = "Pick a user to share with." });

        if (!item.SharedWithUsernames.Any(u => u.Equals(target, StringComparison.OrdinalIgnoreCase)))
            item.SharedWithUsernames.Add(target);

        await _dashboard.SaveAsync(item);
        return NoContent();
    }

    /// <summary>Removes a user from a shared tile's audience. Only the owner can do this.</summary>
    [HttpDelete("{id:guid}/share/{username}")]
    public async Task<IActionResult> Unshare(Guid id, string username)
    {
        var item = await _dashboard.GetByIdAsync(id);
        if (item is null) return NotFound();
        if (!item.Username.Equals(Username, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        item.SharedWithUsernames.RemoveAll(u => u.Equals(username, StringComparison.OrdinalIgnoreCase));
        await _dashboard.SaveAsync(item);
        return NoContent();
    }

    /// <summary>
    /// Lets a RECIPIENT of a shared tile hide it from their own dashboard, without affecting
    /// the owner or anyone else it's shared with, and without revoking the share itself (the
    /// owner can still see it was shared, and could re-share again later). The owner can't
    /// hide their own tile this way - they have unpin for that.
    /// </summary>
    /// <summary>Shared tiles the current user has hidden - lightweight, no live query run.</summary>
    [HttpGet("hidden")]
    public async Task<ActionResult<List<object>>> GetHidden()
    {
        var hidden = await _dashboard.GetHiddenForUserAsync(Username);
        return Ok(hidden.Select(i => (object)new { i.Id, i.Title, OwnerUsername = i.Username, i.GroupByColumn }).ToList());
    }

    /// <summary>Restores a tile the current user previously hid.</summary>
    [HttpPost("{id:guid}/unhide")]
    public async Task<IActionResult> Unhide(Guid id)
    {
        var item = await _dashboard.GetByIdAsync(id);
        if (item is null) return NotFound();
        item.HiddenByUsernames.RemoveAll(u => u.Equals(Username, StringComparison.OrdinalIgnoreCase));
        await _dashboard.SaveAsync(item);
        return NoContent();
    }

    [HttpPost("{id:guid}/hide")]
    public async Task<IActionResult> Hide(Guid id)
    {
        var item = await _dashboard.GetByIdAsync(id);
        if (item is null) return NotFound();
        if (item.Username.Equals(Username, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "You own this tile - use unpin instead of hide." });

        if (!item.HiddenByUsernames.Any(u => u.Equals(Username, StringComparison.OrdinalIgnoreCase)))
            item.HiddenByUsernames.Add(Username);

        await _dashboard.SaveAsync(item);
        return NoContent();
    }
}
