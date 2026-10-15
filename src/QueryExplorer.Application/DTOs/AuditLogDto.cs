namespace QueryExplorer.Application.DTOs;

public class AuditLogEntryResponse
{
    public Guid Id { get; set; }
    public string ActorUsername { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetDescription { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
