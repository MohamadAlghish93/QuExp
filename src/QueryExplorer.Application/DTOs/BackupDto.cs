using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.DTOs;

/// <summary>
/// A full export for moving between environments. Connection passwords are
/// deliberately excluded (they're encrypted with THIS environment's Encryption:Key
/// and would be undecryptable garbage anywhere else) - re-enter them after restore.
/// User password hashes ARE included since PBKDF2 hashes are one-way and safe to
/// move; restored accounts keep their original password.
/// </summary>
public class BackupBundle
{
    public string ExportedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string FormatVersion { get; set; } = "1";

    public List<DbConnectionProfile> Connections { get; set; } = new();
    public List<FavoriteQuery> Favorites { get; set; } = new();
    public List<QueryAlert> Alerts { get; set; } = new();
    public List<User> Users { get; set; } = new();
}

public class RestoreResult
{
    public int ConnectionsImported { get; set; }
    public int FavoritesImported { get; set; }
    public int AlertsImported { get; set; }
    public int UsersImported { get; set; }
}
