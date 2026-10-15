namespace QueryExplorer.Application.DTOs;

public class QueryResultPage
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
    /// <summary>Null until the background COUNT(*) resolves (or if it times out / is skipped).</summary>
    public long? EstimatedTotalCount { get; set; }
    public long ElapsedMs { get; set; }
}
