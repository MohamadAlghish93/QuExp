using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User> CreateAsync(User user);
    Task UpdatePasswordAsync(string username, string passwordHash, string passwordSalt);
    Task<List<User>> GetAllAsync();
    Task UpdateRoleAsync(string username, Domain.Enums.UserRole role);
    Task UpdateAllowedScreensAsync(string username, List<string> screens);
    Task UpdateProfileAsync(string username, string? displayName, string? email, string? groupName);
    Task<List<string>> GetDistinctGroupNamesAsync();
    Task<bool> DeleteAsync(string username);
    Task<int> CountByRoleAsync(Domain.Enums.UserRole role);
}
