using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry);
    Task<List<AuditLogEntry>> GetRecentAsync(int limit, int skip);
}
