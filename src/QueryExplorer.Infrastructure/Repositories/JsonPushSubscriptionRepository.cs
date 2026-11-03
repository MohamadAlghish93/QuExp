using System.Text.Json;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

public class JsonPushSubscriptionRepository : IPushSubscriptionRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonPushSubscriptionRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "push_subscriptions.json");
        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    public async Task AddAsync(PushSubscription subscription)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            // Re-subscribing with the same endpoint (e.g. re-granting permission) replaces the old entry.
            all.RemoveAll(s => s.Endpoint == subscription.Endpoint);
            all.Add(subscription);
            await WriteAllInternalAsync(all);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string username, string endpoint)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(s => s.Endpoint == endpoint &&
                s.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            await WriteAllInternalAsync(all);
        }
        finally { _lock.Release(); }
    }

    public async Task<List<PushSubscription>> GetForUserAsync(string username)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            return all.Where(s => s.Username.Equals(username, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        finally { _lock.Release(); }
    }

    // Called with the lock already held by the caller in AddAsync/RemoveAsync/GetForUserAsync,
    // or standalone from RemovePermanentlyFailed below - never call this without holding _lock.
    private async Task<List<PushSubscription>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<PushSubscription>>(json, JsonOpts) ?? new List<PushSubscription>();
    }

    private async Task WriteAllInternalAsync(List<PushSubscription> all)
        => await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(all, JsonOpts));

    /// <summary>Called by WebPushNotificationSender when a push service reports the subscription is gone (410/404).</summary>
    public async Task RemoveByEndpointAsync(string endpoint)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(s => s.Endpoint == endpoint);
            await WriteAllInternalAsync(all);
        }
        finally { _lock.Release(); }
    }
}
