using System.Text.Json;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>
/// Simple file-backed store for favorites, as requested. All reads/writes
/// go through a SemaphoreSlim to avoid corrupting the file under concurrent
/// requests. Swap this out for a real database later behind the same
/// IFavoriteQueryRepository interface if the app needs to scale to multiple
/// instances (a local JSON file isn't shared across processes/servers).
/// </summary>
public class JsonFavoriteQueryRepository : IFavoriteQueryRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonFavoriteQueryRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "favorites.json");
        if (!File.Exists(_filePath))
            File.WriteAllText(_filePath, "[]");
    }

    public async Task<List<FavoriteQuery>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAllInternalAsync(); }
        finally { _lock.Release(); }
    }

    public async Task<FavoriteQuery?> GetByIdAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(f => f.Id == id);
    }

    public async Task<FavoriteQuery> SaveAsync(FavoriteQuery query)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(f => f.Id == query.Id);
            all.Add(query);
            await WriteAllInternalAsync(all);
            return query;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            var removed = all.RemoveAll(f => f.Id == id) > 0;
            if (removed) await WriteAllInternalAsync(all);
            return removed;
        }
        finally { _lock.Release(); }
    }

    public async Task<List<FavoriteQuery>> SearchAsync(string? text, string? category, bool? templatesOnly, string? ownerUsername)
    {
        var all = await GetAllAsync();
        IEnumerable<FavoriteQuery> query = all;

        if (templatesOnly.HasValue)
            query = query.Where(f => f.IsTemplate == templatesOnly.Value);

        if (!string.IsNullOrWhiteSpace(ownerUsername))
            query = query.Where(f => f.IsTemplate || f.OwnerUsername.Equals(ownerUsername, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(f => string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim();
            query = query.Where(f =>
                f.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                f.Sql.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (f.Description ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                f.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderByDescending(f => f.LastUsedAtUtc ?? f.CreatedAtUtc).ToList();
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        var all = await GetAllAsync();
        return all.Select(f => f.Category)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .OrderBy(c => c)
                   .ToList();
    }

    public async Task<List<string>> GetTagsAsync()
    {
        var all = await GetAllAsync();
        return all.SelectMany(f => f.Tags)
                   .Where(t => !string.IsNullOrWhiteSpace(t))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .OrderBy(t => t)
                   .ToList();
    }

    private async Task<List<FavoriteQuery>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<FavoriteQuery>>(json, JsonOpts) ?? new List<FavoriteQuery>();
    }

    private async Task WriteAllInternalAsync(List<FavoriteQuery> all)
    {
        var json = JsonSerializer.Serialize(all, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public Task<int> RenameCategoryAsync(string oldName, string newName)
    {
        throw new NotImplementedException();
    }

    public Task<int> RenameTagAsync(string oldName, string newName)
    {
        throw new NotImplementedException();
    }
}
