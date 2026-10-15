using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Infrastructure.Services;

/// <summary>
/// Background loop that checks each enabled alert's row count on its own interval and
/// fires an in-app notification (plus an optional webhook) when the condition
/// transitions from not-met to met. Runs inside the API process - no separate worker
/// deployment needed for this starter's scale.
/// </summary>
public class AlertCheckerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertCheckerService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    public AlertCheckerService(
        IServiceScopeFactory scopeFactory, ILogger<AlertCheckerService> logger, IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await CheckDueAlertsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A bug in one check shouldn't kill the whole background loop.
                _logger.LogError(ex, "Alert check cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckDueAlertsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<IAlertNotificationRepository>();
        var connections = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
        var providerFactory = scope.ServiceProvider.GetRequiredService<IQueryProviderFactory>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionStringResolver>();
        var pushSender = scope.ServiceProvider.GetRequiredService<IPushNotificationSender>();

        var due = await alerts.GetDueForCheckAsync();
        foreach (var alert in due)
        {
            await CheckOneAsync(alert, alerts, notifications, connections, providerFactory, resolver, pushSender, ct);
        }
    }

    private async Task CheckOneAsync(
        QueryAlert alert, IAlertRepository alerts, IAlertNotificationRepository notifications,
        IConnectionRepository connections, IQueryProviderFactory providerFactory,
        IConnectionStringResolver resolver, IPushNotificationSender pushSender, CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(alert.ConnectionId);
        if (connection is null)
        {
            alert.LastError = "Connection no longer exists.";
            alert.LastCheckedAtUtc = DateTime.UtcNow;
            await alerts.SaveAsync(alert);
            return;
        }

        try
        {
            var provider = providerFactory.GetProvider(connection.ProviderType);
            var connectionString = resolver.Resolve(connection);
            var parameters = alert.Parameters.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

            var count = await provider.ExecuteCountAsync(connectionString, alert.Sql, parameters, ct);

            var conditionMet = alert.Condition switch
            {
                AlertCondition.RowCountGreaterThan => count > alert.Threshold,
                AlertCondition.RowCountLessThan => count < alert.Threshold,
                AlertCondition.RowCountEquals => count == alert.Threshold,
                _ => false
            };

            alert.LastRowCount = count;
            alert.LastCheckedAtUtc = DateTime.UtcNow;
            alert.LastError = null;

            // Fire only on the transition into "triggered", so a persistently-true
            // condition doesn't create a new notification on every interval.
            if (conditionMet && !alert.WasTriggeredLastCheck)
            {
                await FireAsync(alert, count, notifications, pushSender, ct);
            }
            alert.WasTriggeredLastCheck = conditionMet;
        }
        catch (Exception ex)
        {
            alert.LastError = ex.Message;
            alert.LastCheckedAtUtc = DateTime.UtcNow;
            _logger.LogWarning(ex, "Alert check failed for {AlertId}", alert.Id);
        }

        await alerts.SaveAsync(alert);
    }

    private async Task FireAsync(
        QueryAlert alert, long count, IAlertNotificationRepository notifications, IPushNotificationSender pushSender, CancellationToken ct)
    {
        alert.LastTriggeredAtUtc = DateTime.UtcNow;

        var message = alert.Condition switch
        {
            AlertCondition.RowCountGreaterThan => $"\"{alert.Name}\" returned {count:N0} rows (threshold: more than {alert.Threshold:N0}).",
            AlertCondition.RowCountLessThan => $"\"{alert.Name}\" returned {count:N0} rows (threshold: fewer than {alert.Threshold:N0}).",
            AlertCondition.RowCountEquals => $"\"{alert.Name}\" returned exactly {count:N0} rows.",
            _ => $"\"{alert.Name}\" triggered with {count:N0} rows."
        };

        await notifications.AddAsync(new AlertNotification
        {
            Username = alert.Username,
            AlertId = alert.Id,
            AlertName = alert.Name,
            Message = message,
            RowCount = count
        });

        // Fire-and-forget-ish but awaited so a slow push service doesn't overlap
        // with the next alert's check; delivery itself is still best-effort inside the sender.
        await pushSender.SendToUserAsync(alert.Username, $"Alert: {alert.Name}", message, "/alerts");

        if (!string.IsNullOrWhiteSpace(alert.WebhookUrl))
            await TryCallWebhookAsync(alert, count, message, ct);
    }

    private async Task TryCallWebhookAsync(QueryAlert alert, long count, string message, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(AlertCheckerService));
            client.Timeout = TimeSpan.FromSeconds(10);
            await client.PostAsJsonAsync(alert.WebhookUrl, new
            {
                alertId = alert.Id,
                alertName = alert.Name,
                rowCount = count,
                message,
                triggeredAtUtc = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            // Webhook delivery is best-effort; the in-app notification already exists regardless.
            _logger.LogWarning(ex, "Webhook delivery failed for alert {AlertId}", alert.Id);
        }
    }
}
