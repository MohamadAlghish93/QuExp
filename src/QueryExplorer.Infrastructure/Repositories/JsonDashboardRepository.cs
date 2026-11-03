using System.Text.Json;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

public class JsonDashboardRepository : IDashboardRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonDashboardRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "dashboard.json");
        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    public async Task<List<DashboardItem>> GetForUserAsync(string username)
    {
        var all = await ReadAllAsync();
        return all.Where(i =>
                i.Username.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                (i.SharedWithUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase)) &&
                 !i.HiddenByUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(i => i.Order)
            .ToList();
    }

    public async Task<List<DashboardItem>> GetHiddenForUserAsync(string username)
    {
        var all = await ReadAllAsync();
        return all.Where(i =>
                !i.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
                i.SharedWithUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase)) &&
                i.HiddenByUsernames.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<DashboardItem?> GetByIdAsync(Guid id)
    {
        var all = await ReadAllAsync();
        return all.FirstOrDefault(i => i.Id == id);
    }

    public async Task<DashboardItem> SaveAsync(DashboardItem item)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(i => i.Id == item.Id);
            if (item.Order == 0)
                item.Order = all.Count(i => i.Username.Equals(item.Username, StringComparison.OrdinalIgnoreCase)) + 1;
            all.Add(item);
            await WriteAllInternalAsync(all);
            return item;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string username, Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            var removed = all.RemoveAll(i => i.Id == id && i.Username.Equals(username, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) await WriteAllInternalAsync(all);
            return removed;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<DashboardItem>> ReadAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAllInternalAsync(); }
        finally { _lock.Release(); }
    }

    private async Task<List<DashboardItem>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<DashboardItem>>(json, JsonOpts) ?? new List<DashboardItem>();
    }

    private async Task WriteAllInternalAsync(List<DashboardItem> all)
        => await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(all, JsonOpts));
}
