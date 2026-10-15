namespace QueryExplorer.Application.DTOs;

public class QueryRequest
{
    public Guid ConnectionId { get; set; }
    public string Sql { get; set; } = string.Empty;
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;

    /// <summary>Values for any {{placeholder}} tokens in Sql. Bound as real DB parameters, not string-concatenated.</summary>
    public Dictionary<string, string>? Parameters { get; set; }
}
