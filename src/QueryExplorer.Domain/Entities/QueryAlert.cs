using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Domain.Entities;

/// <summary>
/// A saved query that gets checked periodically by AlertCheckerService. When the row
/// count crosses the configured condition, an in-app notification is created (and,
/// if configured, a webhook is called). Notifications fire on the transition into a
/// triggered state, not on every check, so a persistently-true condition doesn't spam.
/// </summary>
public class QueryAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();

    public AlertCondition Condition { get; set; } = AlertCondition.RowCountGreaterThan;
    public long Threshold { get; set; }
    public int CheckIntervalMinutes { get; set; } = 15;

    /// <summary>Optional: POSTed a small JSON payload when the alert fires. No auth/retry - a lightweight extension point.</summary>
    public string? WebhookUrl { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedAtUtc { get; set; }
    public long? LastRowCount { get; set; }
    public string? LastError { get; set; }
    public bool WasTriggeredLastCheck { get; set; } // tracks edge, so we notify on transition only
    public DateTime? LastTriggeredAtUtc { get; set; }
}
