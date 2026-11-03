using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Services;

/// <summary>
/// Checks for due FavoriteAssignments ("check this query weekly") and creates a
/// reminder notification. Reuses the AlertNotification table/bell as a general-purpose
/// in-app notification feed (AlertId is repurposed to hold the assignment's id) rather
/// than introducing a second notification system end-to-end.
/// </summary>
public class AssignmentReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssignmentReminderService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    public AssignmentReminderService(IServiceScopeFactory scopeFactory, ILogger<AssignmentReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try { await CheckDueAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Assignment reminder cycle failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckDueAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var assignments = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<IAlertNotificationRepository>();
        var pushSender = scope.ServiceProvider.GetRequiredService<IPushNotificationSender>();

        foreach (var assignment in await assignments.GetDueAsync())
        {
            var sqlPreview = assignment.Sql.Length > 80 ? assignment.Sql[..80].Trim() + "…" : assignment.Sql.Trim();
            var message = $"It's your turn to check \"{assignment.FavoriteName}\" (assigned by {assignment.AssignedByUsername})" +
                          (string.IsNullOrWhiteSpace(assignment.Note) ? "" : $": {assignment.Note}") +
                          $" — {sqlPreview}";

            await notifications.AddAsync(new AlertNotification
            {
                Username = assignment.AssignedToUsername,
                AlertId = assignment.Id,
                AlertName = assignment.FavoriteName,
                Message = message,
                RowCount = 0
            });

            await pushSender.SendToUserAsync(assignment.AssignedToUsername, "Query assigned to you", message, "/favorites");

            if (assignment.RepeatIntervalDays <= 0)
            {
                // One-time reminder - deactivate so it doesn't fire again.
                assignment.Active = false;
            }
            else
            {
                // Push the due date forward so it doesn't re-fire every 5-minute poll
                // until the assignee actually marks it done.
                assignment.NextDueAtUtc = DateTime.UtcNow.AddDays(assignment.RepeatIntervalDays);
            }

            await assignments.SaveAsync(assignment);
        }
    }
}
