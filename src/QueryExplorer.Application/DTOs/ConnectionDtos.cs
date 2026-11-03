using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Application.DTOs;

/// <summary>What the browser sends when creating/editing a connection.</summary>
public class SaveConnectionRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DatabaseProviderType ProviderType { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>Plain password from the form. Encrypted before it touches disk.
    /// Leave null when editing to keep the existing stored password unchanged.</summary>
    public string? Password { get; set; }

    public string? ExtraOptions { get; set; }

    /// <summary>Empty/null = visible to everyone. Non-empty = only these usernames (plus any Admin) can see/use it.</summary>
    public List<string>? AllowedUsernames { get; set; }
}

/// <summary>What the browser receives. Deliberately has no password field of any kind.</summary>
public class ConnectionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? ExtraOptions { get; set; }
    public bool HasPassword { get; set; }
    public List<string> AllowedUsernames { get; set; } = new();
}

public class ConnectionHealthResponse
{
    public Guid ConnectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsReachable { get; set; }
    public string? Error { get; set; }
    public DateTime CheckedAtUtc { get; set; }
}
