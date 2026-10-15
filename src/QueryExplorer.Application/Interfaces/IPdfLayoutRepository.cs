using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IPdfLayoutRepository
{
    Task<List<PdfLayoutTemplate>> GetForFavoriteAsync(Guid favoriteId);
    Task<PdfLayoutTemplate?> GetByIdAsync(Guid id);
    Task<PdfLayoutTemplate> SaveAsync(PdfLayoutTemplate layout);
    Task<bool> DeleteAsync(Guid id);
}
