using System.Text.Json;
using System.Text.Json.Serialization;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

/// <summary>
/// Stores connection profiles in connections.json. Passwords are written only in
/// encrypted form (see AesSecretProtector) - this repository never receives or
/// writes a plain password, the caller encrypts before saving.
/// </summary>
public class JsonConnectionRepository : IConnectionRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonConnectionRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "connections.json");
        if (!File.Exists(_filePath))
            File.WriteAllText(_filePath, "[]");
    }

    public async Task<List<DbConnectionProfile>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAllInternalAsync(); }
        finally { _lock.Release(); }
    }

    public async Task<DbConnectionProfile?> GetByIdAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(c => c.Id == id);
    }

    public async Task<DbConnectionProfile> SaveAsync(DbConnectionProfile profile)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(c => c.Id == profile.Id);
            all.Add(profile);
            await WriteAllInternalAsync(all);
            return profile;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            var removed = all.RemoveAll(c => c.Id == id) > 0;
            if (removed) await WriteAllInternalAsync(all);
            return removed;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<DbConnectionProfile>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<DbConnectionProfile>>(json, JsonOpts) ?? new List<DbConnectionProfile>();
    }

    private async Task WriteAllInternalAsync(List<DbConnectionProfile> all)
    {
        var json = JsonSerializer.Serialize(all, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
