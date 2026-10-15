using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>Append-only error log in its own file (errors.db), same pattern as the audit log.</summary>
public class SqliteErrorLogRepository : IErrorLogRepository
{
    private readonly string _connectionString;

    public SqliteErrorLogRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _connectionString = $"Data Source={Path.Combine(dataDirectory, "errors.db")}";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ErrorLog (
                Id TEXT PRIMARY KEY, Username TEXT NOT NULL, Source TEXT NOT NULL,
                Method TEXT NULL, Path TEXT NULL, StatusCode INTEGER NULL,
                Message TEXT NOT NULL, Details TEXT NULL, CreatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ErrorLog_CreatedAtUtc ON ErrorLog (CreatedAtUtc DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task AddAsync(ErrorLogEntry e)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ErrorLog (Id, Username, Source, Method, Path, StatusCode, Message, Details, CreatedAtUtc)
            VALUES ($id, $user, $source, $method, $path, $status, $message, $details, $created);
            """;
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$user", e.Username);
        cmd.Parameters.AddWithValue("$source", e.Source);
        cmd.Parameters.AddWithValue("$method", (object?)e.Method ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$path", (object?)e.Path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (object?)e.StatusCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$message", e.Message);
        cmd.Parameters.AddWithValue("$details", (object?)e.Details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", e.CreatedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ErrorLogEntry>> GetRecentAsync(int limit, int skip, string? source)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Username, Source, Method, Path, StatusCode, Message, Details, CreatedAtUtc
            FROM ErrorLog WHERE ($source IS NULL OR Source = $source)
            ORDER BY CreatedAtUtc DESC LIMIT $limit OFFSET $skip;
            """;
        cmd.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$skip", skip);

        var results = new List<ErrorLogEntry>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            results.Add(new ErrorLogEntry
            {
                Id = Guid.Parse(r.GetString(0)), Username = r.GetString(1), Source = r.GetString(2),
                Method = r.IsDBNull(3) ? null : r.GetString(3), Path = r.IsDBNull(4) ? null : r.GetString(4),
                StatusCode = r.IsDBNull(5) ? null : r.GetInt32(5), Message = r.GetString(6),
                Details = r.IsDBNull(7) ? null : r.GetString(7),
                CreatedAtUtc = DateTime.Parse(r.GetString(8)).ToUniversalTime()
            });
        }
        return results;
    }

    public async Task ClearAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ErrorLog;";
        await cmd.ExecuteNonQueryAsync();
    }
}
