using System.Text.Json;
using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>Saved workflow definitions plus the append-only history of template moves (workflow.db).</summary>
public class SqliteWorkflowRepository : IWorkflowRepository
{
    private readonly string _connectionString;

    public SqliteWorkflowRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _connectionString = $"Data Source={Path.Combine(dataDirectory, "workflow.db")}";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS WorkflowDefinitions (
                Id TEXT PRIMARY KEY, Name TEXT NOT NULL, OwnerUsername TEXT NOT NULL,
                StepsJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS WorkflowHistory (
                Id TEXT PRIMARY KEY, FavoriteId TEXT NOT NULL, Action TEXT NOT NULL,
                FromStepIndex INTEGER NOT NULL, ToStepIndex INTEGER NOT NULL,
                FromStepName TEXT NOT NULL, ToStepName TEXT NOT NULL,
                MovedByUsername TEXT NOT NULL, Note TEXT NULL, MovedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_WorkflowHistory_Favorite ON WorkflowHistory (FavoriteId, MovedAtUtc);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<List<WorkflowDefinition>> GetDefinitionsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, OwnerUsername, StepsJson, CreatedAtUtc FROM WorkflowDefinitions ORDER BY Name;";
        var list = new List<WorkflowDefinition>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapDefinition(r));
        return list;
    }

    public async Task<WorkflowDefinition?> GetDefinitionAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, OwnerUsername, StepsJson, CreatedAtUtc FROM WorkflowDefinitions WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapDefinition(r) : null;
    }

    public async Task<WorkflowDefinition> SaveDefinitionAsync(WorkflowDefinition d)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO WorkflowDefinitions (Id, Name, OwnerUsername, StepsJson, CreatedAtUtc)
            VALUES ($id, $name, $owner, $steps, $created);
            """;
        cmd.Parameters.AddWithValue("$id", d.Id.ToString());
        cmd.Parameters.AddWithValue("$name", d.Name);
        cmd.Parameters.AddWithValue("$owner", d.OwnerUsername);
        cmd.Parameters.AddWithValue("$steps", JsonSerializer.Serialize(d.Steps));
        cmd.Parameters.AddWithValue("$created", d.CreatedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        return d;
    }

    public async Task<bool> DeleteDefinitionAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM WorkflowDefinitions WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task AddHistoryAsync(WorkflowHistoryEntry e)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WorkflowHistory (Id, FavoriteId, Action, FromStepIndex, ToStepIndex, FromStepName, ToStepName, MovedByUsername, Note, MovedAtUtc)
            VALUES ($id, $fav, $action, $from, $to, $fromName, $toName, $by, $note, $at);
            """;
        cmd.Parameters.AddWithValue("$id", e.Id.ToString());
        cmd.Parameters.AddWithValue("$fav", e.FavoriteId.ToString());
        cmd.Parameters.AddWithValue("$action", e.Action);
        cmd.Parameters.AddWithValue("$from", e.FromStepIndex);
        cmd.Parameters.AddWithValue("$to", e.ToStepIndex);
        cmd.Parameters.AddWithValue("$fromName", e.FromStepName);
        cmd.Parameters.AddWithValue("$toName", e.ToStepName);
        cmd.Parameters.AddWithValue("$by", e.MovedByUsername);
        cmd.Parameters.AddWithValue("$note", (object?)e.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", e.MovedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<WorkflowHistoryEntry>> GetHistoryAsync(Guid favoriteId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FavoriteId, Action, FromStepIndex, ToStepIndex, FromStepName, ToStepName, MovedByUsername, Note, MovedAtUtc
            FROM WorkflowHistory WHERE FavoriteId = $fav ORDER BY MovedAtUtc;
            """;
        cmd.Parameters.AddWithValue("$fav", favoriteId.ToString());
        var list = new List<WorkflowHistoryEntry>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new WorkflowHistoryEntry
            {
                Id = Guid.Parse(r.GetString(0)), FavoriteId = Guid.Parse(r.GetString(1)), Action = r.GetString(2),
                FromStepIndex = r.GetInt32(3), ToStepIndex = r.GetInt32(4),
                FromStepName = r.GetString(5), ToStepName = r.GetString(6),
                MovedByUsername = r.GetString(7), Note = r.IsDBNull(8) ? null : r.GetString(8),
                MovedAtUtc = DateTime.Parse(r.GetString(9)).ToUniversalTime()
            });
        }
        return list;
    }

    private static WorkflowDefinition MapDefinition(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), OwnerUsername = r.GetString(2),
        Steps = JsonSerializer.Deserialize<List<WorkflowStep>>(r.GetString(3)) ?? new List<WorkflowStep>(),
        CreatedAtUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime()
    };
}
