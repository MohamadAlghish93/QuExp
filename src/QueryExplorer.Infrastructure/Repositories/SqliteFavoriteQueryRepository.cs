using System.Text.Json;
using Microsoft.Data.Sqlite;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>
/// Favorites/templates, now stored in SQLite (favorites.db) instead of the original
/// favorites.json. On first run, if the table is empty and an old favorites.json is
/// present, its contents are imported once so existing saved queries aren't lost.
/// </summary>
public class SqliteFavoriteQueryRepository : IFavoriteQueryRepository
{
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions JsonOpts = new();

    public SqliteFavoriteQueryRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "favorites.db");
        _connectionString = $"Data Source={dbPath}";
        Initialize(dataDirectory);
    }

    private void Initialize(string dataDirectory)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Favorites (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Sql TEXT NOT NULL,
                    ConnectionId TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Tags TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    LastUsedAtUtc TEXT NULL,
                    OwnerUsername TEXT NOT NULL,
                    IsTemplate INTEGER NOT NULL,
                    Description TEXT NULL,
                    SharedWithUsernames TEXT NOT NULL,
                    ApprovalStatus TEXT NOT NULL DEFAULT 'Approved',
                    HiddenByUsernames TEXT NOT NULL DEFAULT '[]',
                    DateFieldFormats TEXT NOT NULL DEFAULT '[]',
                    WorkflowDefinitionId TEXT NULL,
                    WorkflowName TEXT NULL,
                    WorkflowSteps TEXT NOT NULL DEFAULT '[]',
                    WorkflowStepIndex INTEGER NOT NULL DEFAULT 0
                );
                """;
            createCmd.ExecuteNonQuery();
        }

        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Favorites ADD COLUMN ApprovalStatus TEXT NOT NULL DEFAULT 'Approved';";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already exists */ }

        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Favorites ADD COLUMN HiddenByUsernames TEXT NOT NULL DEFAULT '[]';";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already exists */ }

        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Favorites ADD COLUMN DateFieldFormats TEXT NOT NULL DEFAULT '[]';";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already exists */ }

        foreach (var ddl in new[]
        {
            "ALTER TABLE Favorites ADD COLUMN WorkflowDefinitionId TEXT NULL;",
            "ALTER TABLE Favorites ADD COLUMN WorkflowName TEXT NULL;",
            "ALTER TABLE Favorites ADD COLUMN WorkflowSteps TEXT NOT NULL DEFAULT '[]';",
            "ALTER TABLE Favorites ADD COLUMN WorkflowStepIndex INTEGER NOT NULL DEFAULT 0;"
        })
        {
            try
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = ddl;
                alterCmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* column already exists */ }
        }

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Favorites;";
        var existingCount = Convert.ToInt64(countCmd.ExecuteScalar());

        if (existingCount == 0)
        {
            // One-time migration from the old JSON-file store, so upgrading doesn't lose data.
            var jsonPath = Path.Combine(dataDirectory, "favorites.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = File.ReadAllText(jsonPath);
                    var legacyFavorites = JsonSerializer.Deserialize<List<FavoriteQuery>>(json, JsonOpts);
                    if (legacyFavorites is { Count: > 0 })
                    {
                        foreach (var favorite in legacyFavorites)
                            InsertInternal(conn, favorite);
                    }
                }
                catch (JsonException)
                {
                    // Corrupt/unexpected legacy file - skip the import rather than fail startup.
                }
            }
        }
    }

    private static void InsertInternal(SqliteConnection conn, FavoriteQuery f)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Favorites
                (Id, Name, Sql, ConnectionId, Category, Tags, CreatedAtUtc, LastUsedAtUtc, OwnerUsername, IsTemplate, Description, SharedWithUsernames, ApprovalStatus, HiddenByUsernames, DateFieldFormats, WorkflowDefinitionId, WorkflowName, WorkflowSteps, WorkflowStepIndex)
            VALUES
                ($id, $name, $sql, $connId, $category, $tags, $created, $lastUsed, $owner, $isTemplate, $description, $sharedWith, $approval, $hiddenBy, $dateFormats, $wfId, $wfName, $wfSteps, $wfIndex);
            """;
        cmd.Parameters.AddWithValue("$id", f.Id.ToString());
        cmd.Parameters.AddWithValue("$name", f.Name);
        cmd.Parameters.AddWithValue("$sql", f.Sql);
        cmd.Parameters.AddWithValue("$connId", f.ConnectionId.ToString());
        cmd.Parameters.AddWithValue("$category", f.Category);
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(f.Tags ?? new List<string>()));
        cmd.Parameters.AddWithValue("$created", f.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$lastUsed", (object?)f.LastUsedAtUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$owner", f.OwnerUsername ?? "");
        cmd.Parameters.AddWithValue("$isTemplate", f.IsTemplate ? 1 : 0);
        cmd.Parameters.AddWithValue("$description", (object?)f.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sharedWith", JsonSerializer.Serialize(f.SharedWithUsernames ?? new List<string>()));
        cmd.Parameters.AddWithValue("$approval", f.ApprovalStatus.ToString());
        cmd.Parameters.AddWithValue("$hiddenBy", JsonSerializer.Serialize(f.HiddenByUsernames ?? new List<string>()));
        cmd.Parameters.AddWithValue("$dateFormats", JsonSerializer.Serialize(f.DateFieldFormats ?? new List<DateFieldFormat>()));
        cmd.Parameters.AddWithValue("$wfId", (object?)f.WorkflowDefinitionId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$wfName", (object?)f.WorkflowName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$wfSteps", JsonSerializer.Serialize(f.WorkflowSteps ?? new List<WorkflowStep>()));
        cmd.Parameters.AddWithValue("$wfIndex", f.WorkflowStepIndex);
        cmd.ExecuteNonQuery();
    }

    public async Task<List<FavoriteQuery>> GetAllAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Favorites;";

        var results = new List<FavoriteQuery>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) results.Add(Map(reader));
        return results;
    }

    public async Task<FavoriteQuery?> GetByIdAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Favorites WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<FavoriteQuery> SaveAsync(FavoriteQuery query)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        InsertInternal(conn, query); // INSERT OR REPLACE handles both create and update
        return query;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Favorites WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<FavoriteQuery>> SearchAsync(string? text, string? category, bool? templatesOnly, string? ownerUsername)
    {
        var all = await GetAllAsync();
        IEnumerable<FavoriteQuery> query = all;

        if (templatesOnly.HasValue)
            query = query.Where(f => f.IsTemplate == templatesOnly.Value);

        if (!string.IsNullOrWhiteSpace(ownerUsername))
        {
            query = query.Where(f =>
                f.OwnerUsername.Equals(ownerUsername, StringComparison.OrdinalIgnoreCase) ||
                (f.IsTemplate && IsVisibleTemplate(f, ownerUsername)));
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(f => string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim();
            query = query.Where(f =>
                f.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                f.Sql.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (f.Description ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                f.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderByDescending(f => f.LastUsedAtUtc ?? f.CreatedAtUtc).ToList();
    }

    /// <summary>A template is visible to a given user if it's shared with everyone (empty list),
    /// shared specifically with them, or they own it.</summary>
    private static bool IsVisibleTemplate(FavoriteQuery f, string username)
    {
        var isOwner = f.OwnerUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
        // Not-yet-approved (or rejected) templates are only visible to their own owner,
        // so they can see its status - everyone else waits until an Admin approves it,
        // regardless of who it's shared with.
        if (f.ApprovalStatus != Domain.Enums.TemplateApprovalStatus.Approved && !isOwner) return false;

        // A recipient who chose to hide this template from their own view doesn't see it,
        // even though it's still technically shared with them (the owner's share is untouched).
        if (!isOwner && f.HiddenByUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase)))
            return false;

        return isOwner ||
            f.SharedWithUsernames.Count == 0 ||
            f.SharedWithUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        var all = await GetAllAsync();
        return all.Select(f => f.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
    }

    public async Task<List<string>> GetTagsAsync()
    {
        var all = await GetAllAsync();
        return all.SelectMany(f => f.Tags).Where(t => !string.IsNullOrWhiteSpace(t))
                   .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
    }

    public async Task<int> RenameCategoryAsync(string oldName, string newName)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Favorites SET Category = $newName WHERE Category = $oldName COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$newName", newName);
        cmd.Parameters.AddWithValue("$oldName", oldName);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> RenameTagAsync(string oldName, string newName)
    {
        var all = await GetAllAsync();
        var affected = 0;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        foreach (var favorite in all)
        {
            var index = favorite.Tags.FindIndex(t => t.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;

            favorite.Tags[index] = newName;
            // Collapse an accidental duplicate created by the rename (e.g. renaming "temp" to an existing "temporary").
            favorite.Tags = favorite.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Favorites SET Tags = $tags WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(favorite.Tags));
            cmd.Parameters.AddWithValue("$id", favorite.Id.ToString());
            await cmd.ExecuteNonQueryAsync();
            affected++;
        }

        return affected;
    }

    private static FavoriteQuery Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Sql = reader.GetString(reader.GetOrdinal("Sql")),
        ConnectionId = Guid.Parse(reader.GetString(reader.GetOrdinal("ConnectionId"))),
        Category = reader.GetString(reader.GetOrdinal("Category")),
        Tags = DeserializeList(reader.GetString(reader.GetOrdinal("Tags"))),
        CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))).ToUniversalTime(),
        LastUsedAtUtc = reader.IsDBNull(reader.GetOrdinal("LastUsedAtUtc"))
            ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastUsedAtUtc"))).ToUniversalTime(),
        OwnerUsername = reader.GetString(reader.GetOrdinal("OwnerUsername")),
        IsTemplate = reader.GetInt32(reader.GetOrdinal("IsTemplate")) == 1,
        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
        SharedWithUsernames = DeserializeList(reader.GetString(reader.GetOrdinal("SharedWithUsernames"))),
        ApprovalStatus = Enum.Parse<Domain.Enums.TemplateApprovalStatus>(reader.GetString(reader.GetOrdinal("ApprovalStatus"))),
        HiddenByUsernames = DeserializeList(reader.GetString(reader.GetOrdinal("HiddenByUsernames"))),
        DateFieldFormats = DeserializeDateFormats(reader.GetString(reader.GetOrdinal("DateFieldFormats"))),
        WorkflowDefinitionId = reader.IsDBNull(reader.GetOrdinal("WorkflowDefinitionId"))
            ? null : Guid.Parse(reader.GetString(reader.GetOrdinal("WorkflowDefinitionId"))),
        WorkflowName = reader.IsDBNull(reader.GetOrdinal("WorkflowName")) ? null : reader.GetString(reader.GetOrdinal("WorkflowName")),
        WorkflowSteps = DeserializeSteps(reader.GetString(reader.GetOrdinal("WorkflowSteps"))),
        WorkflowStepIndex = reader.GetInt32(reader.GetOrdinal("WorkflowStepIndex"))
    };

    private static List<WorkflowStep> DeserializeSteps(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<WorkflowStep>();
        try { return JsonSerializer.Deserialize<List<WorkflowStep>>(json) ?? new List<WorkflowStep>(); }
        catch (JsonException) { return new List<WorkflowStep>(); }
    }

    private static List<DateFieldFormat> DeserializeDateFormats(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<DateFieldFormat>();
        try { return JsonSerializer.Deserialize<List<DateFieldFormat>>(json) ?? new List<DateFieldFormat>(); }
        catch (JsonException) { return new List<DateFieldFormat>(); }
    }

    private static List<string> DeserializeList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }
}
