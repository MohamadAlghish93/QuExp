using System.Text.Json;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Repositories;

public class JsonPdfLayoutRepository : IPdfLayoutRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonPdfLayoutRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "pdf_layouts.json");
        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    public async Task<List<PdfLayoutTemplate>> GetForFavoriteAsync(Guid favoriteId)
    {
        var all = await ReadAllAsync();
        return all.Where(l => l.FavoriteId == favoriteId).OrderBy(l => l.Name).ToList();
    }

    public async Task<PdfLayoutTemplate?> GetByIdAsync(Guid id)
    {
        var all = await ReadAllAsync();
        return all.FirstOrDefault(l => l.Id == id);
    }

    public async Task<PdfLayoutTemplate> SaveAsync(PdfLayoutTemplate layout)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            all.RemoveAll(l => l.Id == layout.Id);
            all.Add(layout);
            await WriteAllInternalAsync(all);
            return layout;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllInternalAsync();
            var removed = all.RemoveAll(l => l.Id == id) > 0;
            if (removed) await WriteAllInternalAsync(all);
            return removed;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<PdfLayoutTemplate>> ReadAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAllInternalAsync(); }
        finally { _lock.Release(); }
    }

    private async Task<List<PdfLayoutTemplate>> ReadAllInternalAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<PdfLayoutTemplate>>(json, JsonOpts) ?? new List<PdfLayoutTemplate>();
    }

    private async Task WriteAllInternalAsync(List<PdfLayoutTemplate> all)
        => await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(all, JsonOpts));
}
