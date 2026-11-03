namespace QueryExplorer.Domain.Entities;

public class AlertNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public Guid AlertId { get; set; }
    public string AlertName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
