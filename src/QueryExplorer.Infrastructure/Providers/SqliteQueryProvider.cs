using System.Data;
using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Validation;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Infrastructure.Providers;

public class SqliteQueryProvider : Application.Interfaces.IQueryProvider
{
    public DatabaseProviderType ProviderType => DatabaseProviderType.Sqlite;

    public string BuildConnectionString(
        string host, int port, string database, string username, string password, string? extraOptions)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadOnly
        };

        var connectionString = builder.ToString();
        if (!string.IsNullOrWhiteSpace(extraOptions))
            connectionString += ";" + extraOptions.Trim().TrimStart(';');
        return connectionString;
    }

    public async Task<(bool Ok, string? Error)> TestConnectionAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public string ApplyPaging(string rawSelectSql, int skip, int take)
    {
        var trimmed = rawSelectSql.Trim().TrimEnd(';');
        return $"SELECT * FROM ({trimmed}) AS base_query LIMIT {take} OFFSET {skip}";
    }

    private static string RenderSql(string sql) => QueryParameterHelper.RenderForProvider(sql, "@");

    private static void BindParameters(SqliteCommand command, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new SqliteParameter($"@{name}", value ?? DBNull.Value));
    }

    public async Task<(List<string> Columns, List<Dictionary<string, object?>> Rows)> ExecutePageAsync(
        string connectionString, string sql, int skip, int take,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqliteCommand(ApplyPaging(RenderSql(sql), skip, take), connection)
        {
            CommandTimeout = 60
        };
        BindParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return (columns, rows);
    }

    public async Task<long> ExecuteCountAsync(
        string connectionString, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var trimmed = RenderSql(sql).Trim().TrimEnd(';');
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqliteCommand($"SELECT COUNT(*) FROM ({trimmed}) AS count_query", connection)
        {
            CommandTimeout = 60
        };
        BindParameters(command, parameters);
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<long?> TryGetEstimatedRowCountAsync(
        string connectionString, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var trimmed = RenderSql(sql).Trim().TrimEnd(';');
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(timeoutCts.Token);
            await using var command = new SqliteCommand($"SELECT COUNT(*) FROM ({trimmed}) AS count_query", connection)
            {
                CommandTimeout = 5
            };
            BindParameters(command, parameters);
            var result = await command.ExecuteScalarAsync(timeoutCts.Token);
            return Convert.ToInt64(result);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<(string? Value, long Count)>> ExecuteGroupByAsync(
        string connectionString, string sql, string column, int top,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var quotedColumn = $"\"{column.Replace("\"", "\"\"")}\"";
        var inner = RenderSql(sql).Trim().TrimEnd(';');
        var groupSql =
            $"SELECT {quotedColumn} AS group_value, COUNT(*) AS group_count " +
            $"FROM ({inner}) AS grouped_query " +
            $"GROUP BY {quotedColumn} ORDER BY COUNT(*) DESC LIMIT {top}";

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqliteCommand(groupSql, connection) { CommandTimeout = 120 };
        BindParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<(string?, long)>();
        while (await reader.ReadAsync(ct))
        {
            var value = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
            results.Add((value, Convert.ToInt64(reader.GetValue(1))));
        }
        return results;
    }

    public async IAsyncEnumerable<Dictionary<string, object?>> StreamAllAsync(
        string connectionString, string sql, IReadOnlyDictionary<string, object?> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var trimmed = RenderSql(sql).Trim().TrimEnd(';');
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqliteCommand(trimmed, connection) { CommandTimeout = 0 };
        BindParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            yield return row;
        }
    }
}
