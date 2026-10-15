using System.Text.Json;
using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Security;
using QueryExplorer.Domain.Entities;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>
/// Local SQLite store for login accounts (users.db). Creates the database/table on
/// first run and seeds a default Admin account so there's always a way to log in and
/// then create other users from the Users page.
/// CHANGE THE DEFAULT PASSWORD (or delete the seeded user) before exposing this
/// beyond localhost.
/// </summary>
public class SqliteUserRepository : IUserRepository
{
    private readonly string _connectionString;

    public SqliteUserRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "users.db");
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Users (
                    Id TEXT PRIMARY KEY,
                    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    PasswordHash TEXT NOT NULL,
                    PasswordSalt TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                """;
            createCmd.ExecuteNonQuery();
        }

        // Migrations for databases created before these columns existed - SQLite has no
        // "ADD COLUMN IF NOT EXISTS", so attempt each and swallow the duplicate-column error.
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Users ADD COLUMN Role TEXT NOT NULL DEFAULT 'Viewer';";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already exists */ }

        try
        {
            using var alterCmd = conn.CreateCommand();
            // Empty string, not NULL, so existing rows mean "all screens" (see AllowedScreens doc comment).
            alterCmd.CommandText = "ALTER TABLE Users ADD COLUMN AllowedScreens TEXT NOT NULL DEFAULT '';";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already exists */ }

        foreach (var column in new[] { "DisplayName", "Email", "GroupName" })
        {
            try
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE Users ADD COLUMN {column} TEXT NULL;";
                alterCmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* column already exists */ }
        }

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Users";
        var userCount = Convert.ToInt64(countCmd.ExecuteScalar());

        if (userCount == 0)
        {
            // Seed a default admin so the app is usable out of the box.
            // Log in and change this, or delete the row, before going beyond localhost.
            var (hash, salt) = PasswordHasher.Hash("Admin123!");
            using var seedCmd = conn.CreateCommand();
            seedCmd.CommandText = """
                INSERT INTO Users (Id, Username, PasswordHash, PasswordSalt, Role, AllowedScreens, DisplayName, Email, GroupName, CreatedAtUtc)
                VALUES ($id, 'admin', $hash, $salt, 'Admin', '', NULL, NULL, NULL, $created);
                """;
            seedCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            seedCmd.Parameters.AddWithValue("$hash", hash);
            seedCmd.Parameters.AddWithValue("$salt", salt);
            seedCmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
            seedCmd.ExecuteNonQuery();
        }
        else
        {
            // Self-heal a known migration issue: the ALTER TABLE above defaults Role to
            // 'Viewer' for rows that existed before the Role column did, which could
            // silently demote a pre-existing 'admin' account. If there's no Admin at all,
            // promote the account named 'admin' (or, failing that, the earliest account)
            // so nobody is permanently locked out of user management.
            using var adminCountCmd = conn.CreateCommand();
            adminCountCmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = 'Admin';";
            var adminCount = Convert.ToInt64(adminCountCmd.ExecuteScalar());

            if (adminCount == 0)
            {
                using var promoteCmd = conn.CreateCommand();
                promoteCmd.CommandText = """
                    UPDATE Users SET Role = 'Admin' WHERE Username = (
                        SELECT Username FROM Users
                        ORDER BY (CASE WHEN Username = 'admin' THEN 0 ELSE 1 END), CreatedAtUtc
                        LIMIT 1
                    );
                    """;
                promoteCmd.ExecuteNonQuery();
            }
        }
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash, PasswordSalt, Role, AllowedScreens, DisplayName, Email, GroupName, CreatedAtUtc FROM Users WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$username", username);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<User> CreateAsync(User user)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Users (Id, Username, PasswordHash, PasswordSalt, Role, AllowedScreens, DisplayName, Email, GroupName, CreatedAtUtc)
            VALUES ($id, $username, $hash, $salt, $role, $screens, $displayName, $email, $group, $created);
            """;
        cmd.Parameters.AddWithValue("$id", user.Id.ToString());
        cmd.Parameters.AddWithValue("$username", user.Username);
        cmd.Parameters.AddWithValue("$hash", user.PasswordHash);
        cmd.Parameters.AddWithValue("$salt", user.PasswordSalt);
        cmd.Parameters.AddWithValue("$role", user.Role.ToString());
        cmd.Parameters.AddWithValue("$screens", JsonSerializer.Serialize(user.AllowedScreens ?? new List<string>()));
        cmd.Parameters.AddWithValue("$displayName", (object?)user.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$email", (object?)user.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$group", (object?)user.GroupName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", user.CreatedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        return user;
    }

    public async Task UpdatePasswordAsync(string username, string passwordHash, string passwordSalt)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET PasswordHash = $hash, PasswordSalt = $salt WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$hash", passwordHash);
        cmd.Parameters.AddWithValue("$salt", passwordSalt);
        cmd.Parameters.AddWithValue("$username", username);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<User>> GetAllAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash, PasswordSalt, Role, AllowedScreens, DisplayName, Email, GroupName, CreatedAtUtc FROM Users ORDER BY Username;";

        var results = new List<User>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) results.Add(Map(reader));
        return results;
    }

    public async Task UpdateRoleAsync(string username, UserRole role)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET Role = $role WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$role", role.ToString());
        cmd.Parameters.AddWithValue("$username", username);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateAllowedScreensAsync(string username, List<string> screens)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET AllowedScreens = $screens WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$screens", JsonSerializer.Serialize(screens ?? new List<string>()));
        cmd.Parameters.AddWithValue("$username", username);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateProfileAsync(string username, string? displayName, string? email, string? groupName)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET DisplayName = $displayName, Email = $email, GroupName = $group WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$displayName", (object?)displayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$email", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$group", (object?)groupName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$username", username);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetDistinctGroupNamesAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT GroupName FROM Users WHERE GroupName IS NOT NULL AND GroupName != '' ORDER BY GroupName;";
        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) results.Add(reader.GetString(0));
        return results;
    }

    public async Task<bool> DeleteAsync(string username)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Users WHERE Username = $username;";
        cmd.Parameters.AddWithValue("$username", username);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<int> CountByRoleAsync(UserRole role)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = $role;";
        cmd.Parameters.AddWithValue("$role", role.ToString());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static User Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Username = reader.GetString(1),
        PasswordHash = reader.GetString(2),
        PasswordSalt = reader.GetString(3),
        Role = Enum.Parse<UserRole>(reader.GetString(4)),
        AllowedScreens = ParseScreens(reader.GetString(5)),
        DisplayName = reader.IsDBNull(6) ? null : reader.GetString(6),
        Email = reader.IsDBNull(7) ? null : reader.GetString(7),
        GroupName = reader.IsDBNull(8) ? null : reader.GetString(8),
        CreatedAtUtc = DateTime.Parse(reader.GetString(9)).ToUniversalTime()
    };

    private static List<string> ParseScreens(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); } // tolerate malformed data rather than fail login
    }
}
