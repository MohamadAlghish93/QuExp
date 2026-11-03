using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

/// <summary>Shared "can this user see/use this connection" check, used by every
/// controller that resolves a connection to run a query, alert, or dashboard tile against.</summary>
public static class ConnectionAccessExtensions
{
    public static bool CanAccess(this DbConnectionProfile connection, string username, string role)
    {
        if (role == "Admin") return true;
        if (connection.AllowedUsernames is null || connection.AllowedUsernames.Count == 0) return true; // visible to everyone by default
        return connection.AllowedUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase));
    }
}
