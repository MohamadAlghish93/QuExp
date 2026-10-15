using System.Text.Json;
using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>
/// History is append-heavy and grows continuously, so it's stored in SQLite
/// (history.db) rather than a rewritten JSON file like favorites/connections -
/// much cheaper to append to and to page through as the log grows.
/// </summary>
public class SqliteQueryHistoryRepository : IQueryHistoryRepository
{
    private readonly string _connectionString;

    public SqliteQueryHistoryRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "history.db");
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS QueryHistory (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                ConnectionId TEXT NOT NULL,
                ConnectionName TEXT NOT NULL,
                Sql TEXT NOT NULL,
                ParametersJson TEXT NULL,
                Success INTEGER NOT NULL,
                Error TEXT NULL,
                RowCount INTEGER NULL,
                DurationMs INTEGER NOT NULL,
                ExecutedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_QueryHistory_Username_ExecutedAtUtc
                ON QueryHistory (Username, ExecutedAtUtc DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task AddAsync(QueryHistoryEntry entry)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO QueryHistory
                (Id, Username, ConnectionId, ConnectionName, Sql, ParametersJson, Success, Error, RowCount, DurationMs, ExecutedAtUtc)
            VALUES
                ($id, $username, $connectionId, $connectionName, $sql, $parametersJson, $success, $error, $rowCount, $durationMs, $executedAt);
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$username", entry.Username);
        cmd.Parameters.AddWithValue("$connectionId", entry.ConnectionId.ToString());
        cmd.Parameters.AddWithValue("$connectionName", entry.ConnectionName);
        cmd.Parameters.AddWithValue("$sql", entry.Sql);
        cmd.Parameters.AddWithValue("$parametersJson", (object?)entry.ParametersJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$success", entry.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("$error", (object?)entry.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rowCount", (object?)entry.RowCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$durationMs", entry.DurationMs);
        cmd.Parameters.AddWithValue("$executedAt", entry.ExecutedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<QueryHistoryEntry>> GetRecentAsync(string username, int limit, int skip)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Username, ConnectionId, ConnectionName, Sql, ParametersJson, Success, Error, RowCount, DurationMs, ExecutedAtUtc
            FROM QueryHistory
            WHERE Username = $username
            ORDER BY ExecutedAtUtc DESC
            LIMIT $limit OFFSET $skip;
            """;
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$skip", skip);

        var results = new List<QueryHistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) results.Add(Map(reader));
        return results;
    }

    public async Task<QueryHistoryEntry?> GetByIdAsync(string username, Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Username, ConnectionId, ConnectionName, Sql, ParametersJson, Success, Error, RowCount, DurationMs, ExecutedAtUtc
            FROM QueryHistory WHERE Username = $username AND Id = $id;
            """;
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task ClearAsync(string username)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM QueryHistory WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$username", username);
        await cmd.ExecuteNonQueryAsync();
    }

    private static QueryHistoryEntry Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Username = reader.GetString(1),
        ConnectionId = Guid.Parse(reader.GetString(2)),
        ConnectionName = reader.GetString(3),
        Sql = reader.GetString(4),
        ParametersJson = reader.IsDBNull(5) ? null : reader.GetString(5),
        Success = reader.GetInt32(6) == 1,
        Error = reader.IsDBNull(7) ? null : reader.GetString(7),
        RowCount = reader.IsDBNull(8) ? null : reader.GetInt64(8),
        DurationMs = reader.GetInt64(9),
        ExecutedAtUtc = DateTime.Parse(reader.GetString(10)).ToUniversalTime()
    };
}
