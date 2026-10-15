using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IAssignmentRepository
{
    Task<List<FavoriteAssignment>> GetAssignedToAsync(string username);
    Task<List<FavoriteAssignment>> GetAssignedByAsync(string username);
    Task<FavoriteAssignment?> GetByIdAsync(Guid id);
    Task<FavoriteAssignment> SaveAsync(FavoriteAssignment assignment);
    Task<bool> DeleteAsync(Guid id, string requestingUsername);

    /// <summary>Active assignments whose NextDueAtUtc has passed - used by AssignmentReminderService.</summary>
    Task<List<FavoriteAssignment>> GetDueAsync();
}
