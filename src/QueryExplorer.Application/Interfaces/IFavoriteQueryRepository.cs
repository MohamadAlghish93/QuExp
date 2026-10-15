using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IFavoriteQueryRepository
{
    Task<List<FavoriteQuery>> GetAllAsync();
    Task<FavoriteQuery?> GetByIdAsync(Guid id);
    Task<FavoriteQuery> SaveAsync(FavoriteQuery query);
    Task<bool> DeleteAsync(Guid id);

    /// <summary>
    /// Search across favorites. templatesOnly=true returns only the shared template
    /// library; templatesOnly=false returns only personal (non-template) favorites;
    /// null returns both. ownerUsername further restricts to one owner's items (used
    /// for personal favorites - templates are visible to everyone regardless of owner).
    /// </summary>
    Task<List<FavoriteQuery>> SearchAsync(string? text, string? category, bool? templatesOnly, string? ownerUsername);
    Task<List<string>> GetCategoriesAsync();
    Task<List<string>> GetTagsAsync();

    /// <summary>Renames a category across every favorite that uses it. Admin-only, enforced by the caller.</summary>
    Task<int> RenameCategoryAsync(string oldName, string newName);

    /// <summary>Renames a tag across every favorite that uses it. Admin-only, enforced by the caller.</summary>
    Task<int> RenameTagAsync(string oldName, string newName);
}
