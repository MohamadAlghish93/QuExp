using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IPushSubscriptionRepository
{
    Task AddAsync(PushSubscription subscription);
    Task RemoveAsync(string username, string endpoint);
    Task<List<PushSubscription>> GetForUserAsync(string username);

    /// <summary>Called when a push service reports a subscription is gone (410/404), regardless of which user it belonged to.</summary>
    Task RemoveByEndpointAsync(string endpoint);
}
