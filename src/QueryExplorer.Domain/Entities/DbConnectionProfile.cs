using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Domain.Entities;

/// <summary>
/// A saved connection the user can pick from when running a query.
/// Details are stored as individual fields (entered through the Connections UI)
/// and assembled into a provider-specific connection string at run time.
/// The password is stored ENCRYPTED - see ISecretProtector - and is never
/// returned to the browser.
/// </summary>
public class DbConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DatabaseProviderType ProviderType { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }

    /// <summary>Database name for SQL Server; service name / SID for Oracle.</summary>
    public string Database { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>AES-encrypted password, base64. Decrypted only in-process when opening a connection.</summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>Optional extra key=value pairs appended verbatim to the built connection string.</summary>
    public string? ExtraOptions { get; set; }

    /// <summary>
    /// Usernames allowed to see/use this connection. Empty/null means visible to everyone
    /// (the default, to match previous behavior). Admins can always see every connection
    /// regardless of this list.
    /// </summary>
    public List<string> AllowedUsernames { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
