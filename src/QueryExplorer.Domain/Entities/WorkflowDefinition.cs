namespace QueryExplorer.Domain.Entities;

/// <summary>
/// A reusable, saved workflow: an ordered list of steps a template moves through. Each step is
/// handled by a user group. A template becomes Official (visible to others) once it reaches a
/// step flagged MakesOfficial - so a one-stage or two-stage sign-off is just a matter of which
/// steps carry the flag. When a template starts a workflow it gets its own COPY of the steps, so
/// later edits to this definition never disturb templates already mid-flow.
/// </summary>
public class WorkflowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string OwnerUsername { get; set; } = string.Empty;
    public List<WorkflowStep> Steps { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class WorkflowStep
{
    public string Name { get; set; } = string.Empty;

    /// <summary>User group (User.GroupName) responsible for this step. Empty = the template's owner.</summary>
    public string ResponsibleGroup { get; set; } = string.Empty;

    /// <summary>Reaching this step makes the template Official. Leave false for review/draft stages.</summary>
    public bool MakesOfficial { get; set; }
}

/// <summary>One recorded move of a template through its workflow. Step names are snapshotted
/// so the history still reads correctly even if steps are later renamed.</summary>
public class WorkflowHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FavoriteId { get; set; }
    public string Action { get; set; } = string.Empty; // Started | Forward | Back | Detached
    public int FromStepIndex { get; set; }
    public int ToStepIndex { get; set; }
    public string FromStepName { get; set; } = string.Empty;
    public string ToStepName { get; set; } = string.Empty;
    public string MovedByUsername { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime MovedAtUtc { get; set; } = DateTime.UtcNow;
}
