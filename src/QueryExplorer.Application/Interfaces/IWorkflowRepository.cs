using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

public interface IWorkflowRepository
{
    Task<List<WorkflowDefinition>> GetDefinitionsAsync();
    Task<WorkflowDefinition?> GetDefinitionAsync(Guid id);
    Task<WorkflowDefinition> SaveDefinitionAsync(WorkflowDefinition definition);
    Task<bool> DeleteDefinitionAsync(Guid id);

    Task AddHistoryAsync(WorkflowHistoryEntry entry);
    Task<List<WorkflowHistoryEntry>> GetHistoryAsync(Guid favoriteId);
}
