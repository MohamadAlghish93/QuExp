using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IQueryHistoryRepository
{
    Task AddAsync(QueryHistoryEntry entry);
    Task<List<QueryHistoryEntry>> GetRecentAsync(string username, int limit, int skip);
    Task<QueryHistoryEntry?> GetByIdAsync(string username, Guid id);
    Task ClearAsync(string username);
}
