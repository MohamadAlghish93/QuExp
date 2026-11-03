using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>Append-heavy log, same reasoning as SqliteQueryHistoryRepository - stored in history.db alongside it.</summary>
public class SqliteAlertNotificationRepository : IAlertNotificationRepository
{
    private readonly string _connectionString;

    public SqliteAlertNotificationRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "history.db"); // shares the file with query history; separate table
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AlertNotifications (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                AlertId TEXT NOT NULL,
                AlertName TEXT NOT NULL,
                Message TEXT NOT NULL,
                RowCount INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                IsRead INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_AlertNotifications_Username
                ON AlertNotifications (Username, CreatedAtUtc DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task AddAsync(AlertNotification notification)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AlertNotifications (Id, Username, AlertId, AlertName, Message, RowCount, CreatedAtUtc, IsRead)
            VALUES ($id, $username, $alertId, $alertName, $message, $rowCount, $createdAt, 0);
            """;
        cmd.Parameters.AddWithValue("$id", notification.Id.ToString());
        cmd.Parameters.AddWithValue("$username", notification.Username);
        cmd.Parameters.AddWithValue("$alertId", notification.AlertId.ToString());
        cmd.Parameters.AddWithValue("$alertName", notification.AlertName);
        cmd.Parameters.AddWithValue("$message", notification.Message);
        cmd.Parameters.AddWithValue("$rowCount", notification.RowCount);
        cmd.Parameters.AddWithValue("$createdAt", notification.CreatedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AlertNotification>> GetForUserAsync(string username, int limit)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Username, AlertId, AlertName, Message, RowCount, CreatedAtUtc, IsRead
            FROM AlertNotifications WHERE Username = $username
            ORDER BY CreatedAtUtc DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<AlertNotification>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AlertNotification
            {
                Id = Guid.Parse(reader.GetString(0)),
                Username = reader.GetString(1),
                AlertId = Guid.Parse(reader.GetString(2)),
                AlertName = reader.GetString(3),
                Message = reader.GetString(4),
                RowCount = reader.GetInt64(5),
                CreatedAtUtc = DateTime.Parse(reader.GetString(6)).ToUniversalTime(),
                IsRead = reader.GetInt32(7) == 1
            });
        }
        return results;
    }

    public async Task<int> GetUnreadCountAsync(string username)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AlertNotifications WHERE Username = $username AND IsRead = 0;";
        cmd.Parameters.AddWithValue("$username", username);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task MarkReadAsync(string username, Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE AlertNotifications SET IsRead = 1 WHERE Username = $username AND Id = $id;";
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkAllReadAsync(string username)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE AlertNotifications SET IsRead = 1 WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$username", username);
        await cmd.ExecuteNonQueryAsync();
    }
}
