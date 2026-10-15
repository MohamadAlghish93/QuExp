namespace QueryExplorer.Domain.Entities;

/// <summary>
/// Automatic, passive log of every query run - distinct from Favorites, which are
/// deliberately saved. One entry per "Run query" click (not per page/scroll).
/// </summary>
public class QueryHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string ConnectionName { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public string? ParametersJson { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long? RowCount { get; set; }
    public long DurationMs { get; set; }
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
}
