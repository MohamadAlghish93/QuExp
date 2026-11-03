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
[Route("api/alerts")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly IAlertRepository _alerts;
    private readonly IAlertNotificationRepository _notifications;
    private readonly IConnectionRepository _connections;
    private readonly IQueryProviderFactory _providerFactory;
    private readonly IConnectionStringResolver _resolver;

    public AlertsController(
        IAlertRepository alerts, IAlertNotificationRepository notifications, IConnectionRepository connections,
        IQueryProviderFactory providerFactory, IConnectionStringResolver resolver)
    {
        _alerts = alerts;
        _notifications = notifications;
        _connections = connections;
        _providerFactory = providerFactory;
        _resolver = resolver;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";

    [HttpGet]
    public async Task<ActionResult<List<AlertResponse>>> GetAll()
    {
        var alerts = await _alerts.GetForUserAsync(Username);
        var connections = await _connections.GetAllAsync();
        return Ok(alerts.Select(a => ToResponse(a, connections)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AlertResponse>> Save([FromBody] SaveAlertRequest request)
    {
        var (isValid, error) = SqlValidator.Validate(request.Sql);
        if (!isValid) return BadRequest(new { error });
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });

        var existing = request.Id.HasValue ? await _alerts.GetByIdAsync(request.Id.Value) : null;
        if (existing is not null && !existing.Username.Equals(Username, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var targetConnection = await _connections.GetByIdAsync(request.ConnectionId);
        if (targetConnection is null) return BadRequest(new { error = "Unknown connection id." });
        if (!targetConnection.CanAccess(Username, Role)) return Forbid();

        var alert = existing ?? new QueryAlert { Username = Username };
        alert.Name = request.Name.Trim();
        alert.ConnectionId = request.ConnectionId;
        alert.Sql = request.Sql;
        alert.Parameters = request.Parameters ?? new Dictionary<string, string>();
        alert.Condition = request.Condition;
        alert.Threshold = request.Threshold;
        alert.CheckIntervalMinutes = Math.Clamp(request.CheckIntervalMinutes, 1, 1440);
        alert.WebhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim();
        alert.Enabled = request.Enabled;

        var saved = await _alerts.SaveAsync(alert);
        var connections = await _connections.GetAllAsync();
        return Ok(ToResponse(saved, connections));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var removed = await _alerts.DeleteAsync(Username, id);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>Runs the check immediately and always notifies (bypassing the edge-trigger logic) so the user can verify setup.</summary>
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(id);
        if (alert is null || !alert.Username.Equals(Username, StringComparison.OrdinalIgnoreCase)) return NotFound();

        var connection = await _connections.GetByIdAsync(alert.ConnectionId);
        if (connection is null) return BadRequest(new { error = "Connection not found." });
        if (!connection.CanAccess(Username, Role)) return Forbid();

        try
        {
            var provider = _providerFactory.GetProvider(connection.ProviderType);
            var connectionString = _resolver.Resolve(connection);
            var parameters = alert.Parameters.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            var count = await provider.ExecuteCountAsync(connectionString, alert.Sql, parameters, ct);

            var conditionMet = alert.Condition switch
            {
                AlertCondition.RowCountGreaterThan => count > alert.Threshold,
                AlertCondition.RowCountLessThan => count < alert.Threshold,
                AlertCondition.RowCountEquals => count == alert.Threshold,
                _ => false
            };

            await _notifications.AddAsync(new AlertNotification
            {
                Username = Username,
                AlertId = alert.Id,
                AlertName = alert.Name,
                Message = $"[Test] \"{alert.Name}\" returned {count:N0} rows - condition {(conditionMet ? "MET" : "not met")}.",
                RowCount = count
            });

            return Ok(new { rowCount = count, conditionMet });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static AlertResponse ToResponse(QueryAlert a, List<Domain.Entities.DbConnectionProfile> connections) => new()
    {
        Id = a.Id,
        Name = a.Name,
        ConnectionId = a.ConnectionId,
        ConnectionName = connections.FirstOrDefault(c => c.Id == a.ConnectionId)?.Name ?? "(connection removed)",
        Sql = a.Sql,
        Parameters = a.Parameters,
        Condition = a.Condition.ToString(),
        Threshold = a.Threshold,
        CheckIntervalMinutes = a.CheckIntervalMinutes,
        WebhookUrl = a.WebhookUrl,
        Enabled = a.Enabled,
        LastCheckedAtUtc = a.LastCheckedAtUtc,
        LastRowCount = a.LastRowCount,
        LastError = a.LastError,
        LastTriggeredAtUtc = a.LastTriggeredAtUtc
    };
}
