using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IConnectionRepository
{
    Task<List<DbConnectionProfile>> GetAllAsync();
    Task<DbConnectionProfile?> GetByIdAsync(Guid id);
    Task<DbConnectionProfile> SaveAsync(DbConnectionProfile profile);
    Task<bool> DeleteAsync(Guid id);
}
