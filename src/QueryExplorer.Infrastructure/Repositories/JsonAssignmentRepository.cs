using System.Text.Json;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

public class JsonAssignmentRepository : IAssignmentRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonAssignmentRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "assignments.json");
        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    public async Task<List<FavoriteAssignment>> GetAssignedToAsync(string username)
    {
        var all = await ReadAllAsync();
        return all.Where(a => a.Active && a.AssignedToUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                   .OrderBy(a => a.NextDueAtUtc)
                   .ToList();
    }

    public async Task<List<FavoriteAssignment>> GetAssignedByAsync(string username)
    {
        var all = await ReadAllAsync();
        return all.Where(a => a.Active && a.AssignedByUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(a => a.CreatedAtUtc)
                   .ToList();
    }

    public async Task<FavoriteAssignment?> GetByIdAsync(Guid id)
    {
        var all = await ReadAllAsync();
        return all.FirstOrDefault(a => a.Id == id);
    }

    public async Task<FavoriteAssignment> SaveAsync(FavoriteAssignment assignment)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(a => a.Id == assignment.Id);
            all.Add(assignment);
            await WriteAllInternalAsync(all);
            return assignment;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, string requestingUsername)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            var removed = all.RemoveAll(a => a.Id == id &&
                (a.AssignedByUsername.Equals(requestingUsername, StringComparison.OrdinalIgnoreCase) ||
                 a.AssignedToUsername.Equals(requestingUsername, StringComparison.OrdinalIgnoreCase))) > 0;
            if (removed) await WriteAllInternalAsync(all);
            return removed;
        }
        finally { _lock.Release(); }
    }

    public async Task<List<FavoriteAssignment>> GetDueAsync()
    {
        var all = await ReadAllAsync();
        var now = DateTime.UtcNow;
        return all.Where(a => a.Active && a.NextDueAtUtc <= now).ToList();
    }

    private async Task<List<FavoriteAssignment>> ReadAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAllInternalAsync(); }
        finally { _lock.Release(); }
    }

    private async Task<List<FavoriteAssignment>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<FavoriteAssignment>>(json, JsonOpts) ?? new List<FavoriteAssignment>();
    }

    private async Task WriteAllInternalAsync(List<FavoriteAssignment> all)
        => await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(all, JsonOpts));
}
