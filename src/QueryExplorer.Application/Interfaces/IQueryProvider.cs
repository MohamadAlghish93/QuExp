using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Application.Interfaces;

/// <summary>
/// One implementation per database technology. To support a new database:
/// 1. Add a value to DatabaseProviderType
/// 2. Implement this interface in Infrastructure/Providers
/// 3. Register it in Program.cs
/// Nothing else in the app needs to change.
/// </summary>
public interface IQueryProvider
{
    DatabaseProviderType ProviderType { get; }

    /// <summary>
    /// Assembles a provider-specific connection string from the individual fields
    /// entered in the Connections UI. The password arrives already decrypted and
    /// is never persisted in this form.
    /// </summary>
    string BuildConnectionString(string host, int port, string database, string username, string password, string? extraOptions);

    /// <summary>Opens and immediately closes a connection to verify the details work.</summary>
    Task<(bool Ok, string? Error)> TestConnectionAsync(string connectionString, CancellationToken ct);

    /// <summary>Wraps the user's raw SELECT with this database's paging syntax.</summary>
    string ApplyPaging(string rawSelectSql, int skip, int take);

    /// <summary>
    /// Runs the paged query and streams rows back without buffering the full result set.
    /// <paramref name="parameters"/> holds values for any {{name}} placeholders in sql -
    /// implementations must bind these as real database parameters, never by
    /// string-concatenating the value into the SQL text.
    /// </summary>
    Task<(List<string> Columns, List<Dictionary<string, object?>> Rows)> ExecutePageAsync(
        string connectionString, string sql, int skip, int take,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);

    /// <summary>Best-effort row count for the UI. Implementations should apply their own timeout
    /// and return null rather than let this block the page request.</summary>
    Task<long?> TryGetEstimatedRowCountAsync(
        string connectionString, string sql,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);

    /// <summary>
    /// Exact row count with a longer timeout than TryGetEstimatedRowCountAsync's fast
    /// UI-facing check - used by alert checks, which run in the background and can
    /// afford to wait. Throws on failure/timeout so the caller can record the error,
    /// rather than silently returning null.
    /// </summary>
    Task<long> ExecuteCountAsync(
        string connectionString, string sql,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);

    /// <summary>Streams every row of the query (no paging) for CSV export, one row at a time.</summary>
    IAsyncEnumerable<Dictionary<string, object?>> StreamAllAsync(
        string connectionString, string sql,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);

    /// <summary>
    /// Runs GROUP BY / COUNT(*) over the full result set of the user's query (not just the
    /// loaded page), returning the top N groups by count. <paramref name="column"/> has already
    /// been validated as a plain identifier by the caller; implementations must still quote it
    /// using their own identifier syntax.
    /// </summary>
    Task<List<(string? Value, long Count)>> ExecuteGroupByAsync(
        string connectionString, string sql, string column, int top,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);
}
