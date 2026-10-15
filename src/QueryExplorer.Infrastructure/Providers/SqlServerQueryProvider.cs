using System.Data;
using Microsoft.Data.SqlClient;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Validation;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Infrastructure.Providers;

public class SqlServerQueryProvider : Application.Interfaces.IQueryProvider
{
    public DatabaseProviderType ProviderType => DatabaseProviderType.SqlServer;

    public string BuildConnectionString(
        string host, int port, string database, string username, string password, string? extraOptions)
    {
        // SqlConnectionStringBuilder escapes values correctly, so a password
        // containing ; or " can't break out and inject extra keywords.
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = port > 0 ? $"{host},{port}" : host,
            InitialCatalog = database,
            UserID = username,
            Password = password,
            TrustServerCertificate = true,
            ConnectTimeout = 15
        };

        var connectionString = builder.ConnectionString;
        if (!string.IsNullOrWhiteSpace(extraOptions))
            connectionString += ";" + extraOptions.Trim().TrimStart(';');
        return connectionString;
    }

    public async Task<(bool Ok, string? Error)> TestConnectionAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // SQL Server needs ORDER BY for OFFSET/FETCH; (SELECT NULL) keeps original
    // ordering intact when the user's query doesn't specify one.
    public string ApplyPaging(string rawSelectSql, int skip, int take)
    {
        var trimmed = rawSelectSql.Trim().TrimEnd(';');
        return $"SELECT * FROM ({trimmed}) AS base_query " +
               $"ORDER BY (SELECT NULL) OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY";
    }

    // {{name}} placeholders become @name and are bound as real parameters -
    // values never get concatenated into the SQL text.
    private static string RenderSql(string sql) => QueryParameterHelper.RenderForProvider(sql, "@");

    private static void BindParameters(SqlCommand cmd, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue($"@{name}", value ?? DBNull.Value);
    }

    public async Task<(List<string> Columns, List<Dictionary<string, object?>> Rows)> ExecutePageAsync(
        string connectionString, string sql, int skip, int take,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var pagedSql = ApplyPaging(RenderSql(sql), skip, take);
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(pagedSql, conn) { CommandTimeout = 60 };
        BindParameters(cmd, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return (columns, rows);
    }

    public async Task<long> ExecuteCountAsync(
        string connectionString, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var trimmed = RenderSql(sql).Trim().TrimEnd(';');
        var countSql = $"SELECT COUNT_BIG(*) FROM ({trimmed}) AS count_query";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(countSql, conn) { CommandTimeout = 60 };
        BindParameters(cmd, parameters);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : Convert.ToInt64(result);
    }

    public async Task<long?> TryGetEstimatedRowCountAsync(
        string connectionString, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var trimmed = RenderSql(sql).Trim().TrimEnd(';');
            var countSql = $"SELECT COUNT_BIG(*) FROM ({trimmed}) AS count_query";

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(timeoutCts.Token);
            await using var cmd = new SqlCommand(countSql, conn) { CommandTimeout = 5 };
            BindParameters(cmd, parameters);
            var result = await cmd.ExecuteScalarAsync(timeoutCts.Token);
            return result is long l ? l : Convert.ToInt64(result);
        }
        catch
        {
            // Count is a nice-to-have; never let it fail the actual query.
            return null;
        }
    }

    public async Task<List<(string? Value, long Count)>> ExecuteGroupByAsync(
        string connectionString, string sql, string column, int top,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        // column is validated as a plain identifier by the caller; bracket-quote it here.
        // Column names can't be bound as parameters - they're part of the SQL structure.
        var quotedColumn = $"[{column.Replace("]", "]]")}]";
        var inner = RenderSql(sql).Trim().TrimEnd(';');

        // TOP (not OFFSET/FETCH) so the ORDER BY stays valid without a wrapping derived table.
        var groupSql =
            $"SELECT TOP ({top}) {quotedColumn} AS group_value, COUNT_BIG(*) AS group_count " +
            $"FROM ({inner}) AS grouped_query " +
            $"GROUP BY {quotedColumn} ORDER BY COUNT_BIG(*) DESC";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(groupSql, conn) { CommandTimeout = 120 };
        BindParameters(cmd, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<(string?, long)>();
        while (await reader.ReadAsync(ct))
        {
            var value = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
            results.Add((value, reader.GetInt64(1)));
        }
        return results;
    }

    public async IAsyncEnumerable<Dictionary<string, object?>> StreamAllAsync(
        string connectionString, string sql, IReadOnlyDictionary<string, object?> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var trimmed = RenderSql(sql).Trim().TrimEnd(';');
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(trimmed, conn) { CommandTimeout = 0 }; // no timeout for full export
        BindParameters(cmd, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            yield return row;
        }
    }
}
