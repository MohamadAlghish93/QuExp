using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Application.DTOs;

public class SaveAlertRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }
    public AlertCondition Condition { get; set; }
    public long Threshold { get; set; }
    public int CheckIntervalMinutes { get; set; } = 15;
    public string? WebhookUrl { get; set; }
    public bool Enabled { get; set; } = true;
}

public class AlertResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string ConnectionName { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string Condition { get; set; } = string.Empty;
    public long Threshold { get; set; }
    public int CheckIntervalMinutes { get; set; }
    public string? WebhookUrl { get; set; }
    public bool Enabled { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
    public long? LastRowCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastTriggeredAtUtc { get; set; }
}

public class NotificationResponse
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public string AlertName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
}
