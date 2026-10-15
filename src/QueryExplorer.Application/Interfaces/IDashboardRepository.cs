using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IDashboardRepository
{
    /// <summary>Items owned by this user, plus items others have shared with them.</summary>
    Task<List<DashboardItem>> GetForUserAsync(string username);
    Task<DashboardItem?> GetByIdAsync(Guid id);

    /// <summary>Shared tiles this user previously hid, so they can be restored.</summary>
    Task<List<DashboardItem>> GetHiddenForUserAsync(string username);
    Task<DashboardItem> SaveAsync(DashboardItem item);
    Task<bool> DeleteAsync(string username, Guid id);
}
