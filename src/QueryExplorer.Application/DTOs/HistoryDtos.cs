namespace QueryExplorer.Application.DTOs;

public class HistoryEntryResponse
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string ConnectionName { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long? RowCount { get; set; }
    public long DurationMs { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
}
