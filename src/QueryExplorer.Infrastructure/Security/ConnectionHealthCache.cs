using System.Collections.Concurrent;
using QueryExplorer.Application.Interfaces;

namespace QueryExplorer.Infrastructure.Security;

/// <summary>
/// In-memory cache of connection test results, keyed per user. Entries live for the
/// configured lifetime so the dashboard tests connectivity once per login session
/// rather than on every page load. Saving or deleting a connection invalidates it.
/// </summary>
public class ConnectionHealthCache : IConnectionHealthCache
{
    private readonly ConcurrentDictionary<string, (List<ConnectionHealth> Results, DateTime ExpiresAtUtc)> _cache = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public bool TryGet(string userKey, out List<ConnectionHealth> results)
    {
        if (_cache.TryGetValue(userKey, out var entry) && entry.ExpiresAtUtc > DateTime.UtcNow)
        {
            results = entry.Results;
            return true;
        }
        _cache.TryRemove(userKey, out _);
        results = new List<ConnectionHealth>();
        return false;
    }

    public void Set(string userKey, List<ConnectionHealth> results)
        => _cache[userKey] = (results, DateTime.UtcNow.Add(Lifetime));

    public void Invalidate(string userKey) => _cache.TryRemove(userKey, out _);
}
