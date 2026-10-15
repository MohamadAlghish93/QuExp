using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>Append-only log of admin actions - same pattern as SqliteQueryHistoryRepository, kept in its own file (audit.db) since it's a distinct, more sensitive concern.</summary>
public class SqliteAuditLogRepository : IAuditLogRepository
{
    private readonly string _connectionString;

    public SqliteAuditLogRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "audit.db");
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AuditLog (
                Id TEXT PRIMARY KEY,
                ActorUsername TEXT NOT NULL,
                Action TEXT NOT NULL,
                TargetDescription TEXT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_AuditLog_CreatedAtUtc ON AuditLog (CreatedAtUtc DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task AddAsync(AuditLogEntry entry)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AuditLog (Id, ActorUsername, Action, TargetDescription, CreatedAtUtc)
            VALUES ($id, $actor, $action, $target, $created);
            """;
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("$actor", entry.ActorUsername);
        cmd.Parameters.AddWithValue("$action", entry.Action);
        cmd.Parameters.AddWithValue("$target", (object?)entry.TargetDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", entry.CreatedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AuditLogEntry>> GetRecentAsync(int limit, int skip)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ActorUsername, Action, TargetDescription, CreatedAtUtc
            FROM AuditLog ORDER BY CreatedAtUtc DESC LIMIT $limit OFFSET $skip;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$skip", skip);

        var results = new List<AuditLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AuditLogEntry
            {
                Id = Guid.Parse(reader.GetString(0)),
                ActorUsername = reader.GetString(1),
                Action = reader.GetString(2),
                TargetDescription = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAtUtc = DateTime.Parse(reader.GetString(4)).ToUniversalTime()
            });
        }
        return results;
    }
}
