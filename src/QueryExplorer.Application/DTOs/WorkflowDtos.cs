using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.DTOs;

public class SaveWorkflowDefinitionRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class WorkflowDefinitionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerUsername { get; set; } = string.Empty;
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class StartWorkflowRequest
{
    public Guid DefinitionId { get; set; }
    public string? Note { get; set; }
}

public class MoveWorkflowRequest
{
    public int ToStepIndex { get; set; }
    public string? Note { get; set; }
}

/// <summary>A template in a workflow, as shown in the workflow inbox.</summary>
public class WorkflowItemResponse
{
    public Guid FavoriteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string OwnerUsername { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public List<WorkflowStep> Steps { get; set; } = new();
    public int CurrentStepIndex { get; set; }
    public bool IsOfficial { get; set; }
    public bool CanAct { get; set; }
}

public class WorkflowInboxResponse
{
    public List<WorkflowItemResponse> WaitingForMe { get; set; } = new();
    public List<WorkflowItemResponse> Other { get; set; } = new();
}
