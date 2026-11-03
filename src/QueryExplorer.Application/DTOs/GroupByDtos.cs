namespace QueryExplorer.Application.DTOs;

public class GroupByRequest
{
    public Guid ConnectionId { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }

    /// <summary>Max distinct groups to return. Protects the UI from a group-by on a high-cardinality column.</summary>
    public int Top { get; set; } = 100;
}

public class GroupByRow
{
    public string? Value { get; set; }
    public long Count { get; set; }
}

public class GroupByResult
{
    public string Column { get; set; } = string.Empty;
    public List<GroupByRow> Groups { get; set; } = new();
    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
}
