using System.Text.Json;
using System.Text.Json.Serialization;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

public class JsonAlertRepository : IAlertRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonAlertRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "alerts.json");
        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    public async Task<List<QueryAlert>> GetForUserAsync(string username)
    {
        var all = await ReadAllAsync();
        return all.Where(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(a => a.CreatedAtUtc)
                   .ToList();
    }

    public async Task<QueryAlert?> GetByIdAsync(Guid id)
    {
        var all = await ReadAllAsync();
        return all.FirstOrDefault(a => a.Id == id);
    }

    public async Task<QueryAlert> SaveAsync(QueryAlert alert)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(a => a.Id == alert.Id);
            all.Add(alert);
            await WriteAllInternalAsync(all);
            return alert;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string username, Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            var removed = all.RemoveAll(a => a.Id == id && a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) await WriteAllInternalAsync(all);
            return removed;
        }
        finally { _lock.Release(); }
    }

    public async Task<List<QueryAlert>> GetDueForCheckAsync()
    {
        var all = await ReadAllAsync();
        var now = DateTime.UtcNow;
        return all.Where(a => a.Enabled &&
                (a.LastCheckedAtUtc == null || now - a.LastCheckedAtUtc.Value >= TimeSpan.FromMinutes(a.CheckIntervalMinutes)))
            .ToList();
    }

    private async Task<List<QueryAlert>> ReadAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAllInternalAsync(); }
        finally { _lock.Release(); }
    }

    private async Task<List<QueryAlert>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<QueryAlert>>(json, JsonOpts) ?? new List<QueryAlert>();
    }

    private async Task WriteAllInternalAsync(List<QueryAlert> all)
        => await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(all, JsonOpts));
}
