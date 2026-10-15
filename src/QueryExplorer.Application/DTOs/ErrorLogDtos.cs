namespace QueryExplorer.Application.DTOs;

/// <summary>What the browser sends when an error is about to be shown to the user.</summary>
public class ReportClientErrorRequest
{
    public string Message { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Method { get; set; }
    public int? StatusCode { get; set; }
}

public class ErrorLogEntryResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
