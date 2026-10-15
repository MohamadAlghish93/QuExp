namespace QueryExplorer.Application.DTOs;

public class CreateAssignmentRequest
{
    public Guid FavoriteId { get; set; }
    public string AssignedToUsername { get; set; } = string.Empty;
    public string? Note { get; set; }
    public int RepeatIntervalDays { get; set; } // 0 = one-time
}

public class AssignmentResponse
{
    public Guid Id { get; set; }
    public Guid FavoriteId { get; set; }
    public string FavoriteName { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string AssignedByUsername { get; set; } = string.Empty;
    public string AssignedToUsername { get; set; } = string.Empty;
    public string? Note { get; set; }
    public int RepeatIntervalDays { get; set; }
    public DateTime NextDueAtUtc { get; set; }
    public DateTime? LastCompletedAtUtc { get; set; }
    public List<DateTime> CompletionHistory { get; set; } = new();
    public bool Active { get; set; }
}
