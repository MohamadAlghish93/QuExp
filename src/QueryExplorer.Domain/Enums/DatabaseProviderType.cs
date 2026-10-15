namespace QueryExplorer.Domain.Enums;

/// <summary>
/// Identifies which IQueryProvider implementation should handle a connection.
/// Add a new value here (and a matching provider in Infrastructure) to support a new database.
/// </summary>
public enum DatabaseProviderType
{
    SqlServer,
    Oracle
    // PostgreSql, MySql, etc. can be added later without touching existing providers.
}
