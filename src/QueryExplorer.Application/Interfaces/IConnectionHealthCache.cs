namespace QueryExplorer.Application.Interfaces;

public record ConnectionHealth(Guid ConnectionId, string Name, bool IsReachable, string? Error, DateTime CheckedAtUtc);

/// <summary>
/// Caches connection test results per user so the dashboard only tests connectivity
/// once per login session instead of on every page load.
/// </summary>
public interface IConnectionHealthCache
{
    bool TryGet(string userKey, out List<ConnectionHealth> results);
    void Set(string userKey, List<ConnectionHealth> results);
    void Invalidate(string userKey);
}
