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
    /// <summary>Target STAGE index (steps that run in parallel share one stage).</summary>
    public int ToStepIndex { get; set; }
    public string? Note { get; set; }
}

public class PassWorkflowRequest
{
    public int StepIndex { get; set; }
    public string? Note { get; set; }
}

public class ApproveWorkflowRequest
{
    /// <summary>Which step to approve. May be omitted when the current stage has only one step.</summary>
    public int? StepIndex { get; set; }
    public string? Note { get; set; }
}

public class RevokeWorkflowRequest
{
    public int StepIndex { get; set; }
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

    /// <summary>Current stage (parallel steps share a stage).</summary>
    public int CurrentStageIndex { get; set; }
    public List<int> PassedSteps { get; set; } = new();
    public List<WorkflowApproval> Approvals { get; set; } = new();
    public bool IsOfficial { get; set; }

    /// <summary>Steps of the current stage this user can pass (they're responsible and it isn't passed yet).</summary>
    public List<int> ActionableSteps { get; set; } = new();

    /// <summary>Steps of the current stage this user can approve right now.</summary>
    public List<int> ApprovableSteps { get; set; } = new();
    public bool CanAct { get; set; }
    public bool CanGoBack { get; set; }

    /// <summary>Admin only: there is an approval on the current stage that can be revoked.</summary>
    public bool CanRevoke { get; set; }
}

public class WorkflowInboxResponse
{
    public List<WorkflowItemResponse> WaitingForMe { get; set; } = new();
    public List<WorkflowItemResponse> Other { get; set; } = new();
}
