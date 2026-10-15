using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IAlertRepository
{
    Task<List<QueryAlert>> GetForUserAsync(string username);
    Task<QueryAlert?> GetByIdAsync(Guid id);
    Task<QueryAlert> SaveAsync(QueryAlert alert);
    Task<bool> DeleteAsync(string username, Guid id);

    /// <summary>All enabled alerts across all users due for a check right now - used by AlertCheckerService.</summary>
    Task<List<QueryAlert>> GetDueForCheckAsync();
}
