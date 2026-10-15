using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IErrorLogRepository
{
    Task AddAsync(ErrorLogEntry entry);
    Task<List<ErrorLogEntry>> GetRecentAsync(int limit, int skip, string? source);
    Task ClearAsync();
}
