namespace QueryExplorer.Domain.Entities;

/// <summary>
/// An error worth an admin's attention. Source "Server" = an unhandled exception caught by the
/// global middleware (with a stack trace in Details). Source "Client" = an error a user actually
/// saw in the UI (reported by the browser), e.g. a rejected request - so admins see what users see.
/// </summary>
public class ErrorLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Source { get; set; } = "Server";
    public string? Method { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
