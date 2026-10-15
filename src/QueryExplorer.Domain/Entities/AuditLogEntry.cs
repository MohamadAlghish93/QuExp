namespace QueryExplorer.Domain.Entities;

/// <summary>Record of an admin-level action - who did what, to whom/what, and when.</summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorUsername { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // e.g. "UserRoleChanged", "CategoryRenamed"
    public string? TargetDescription { get; set; }      // e.g. "bob -> Editor" or "Reports -> Sales Reports"
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
