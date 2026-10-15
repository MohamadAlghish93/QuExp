using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IAlertNotificationRepository
{
    Task AddAsync(AlertNotification notification);
    Task<List<AlertNotification>> GetForUserAsync(string username, int limit);
    Task<int> GetUnreadCountAsync(string username);
    Task MarkReadAsync(string username, Guid id);
    Task MarkAllReadAsync(string username);
}
