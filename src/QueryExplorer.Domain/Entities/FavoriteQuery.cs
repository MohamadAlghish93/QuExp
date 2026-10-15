namespace QueryExplorer.Domain.Entities;

public class FavoriteQuery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Category { get; set; } = "Uncategorized";
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }

    public string OwnerUsername { get; set; } = string.Empty;

    /// <summary>
    /// True for entries in the shared "Query templates" library - curated, parameterized
    /// queries meant for anyone (including non-SQL-fluent users) to find and run.
    /// Only Editors/Admins can create or edit templates; everyone can run them.
    /// </summary>
    public bool IsTemplate { get; set; }

    /// <summary>Plain-language explanation shown on the Templates page, e.g. "Look up a customer by email or phone."</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Only meaningful when IsTemplate is true. Empty = shared with everyone (the
    /// original template behavior). Non-empty = shared with only these specific
    /// usernames (plus the owner and any Admin, who can always manage it).
    /// </summary>
    public List<string> SharedWithUsernames { get; set; } = new();

    /// <summary>
    /// Only meaningful when IsTemplate is true. New templates created by an Editor start
    /// Pending until an Admin approves them; Admin-created templates auto-approve. Non-owners
    /// never see a Pending/Rejected template regardless of sharing - the owner still can,
    /// so they know its status.
    /// </summary>
    public QueryExplorer.Domain.Enums.TemplateApprovalStatus ApprovalStatus { get; set; } = QueryExplorer.Domain.Enums.TemplateApprovalStatus.Approved;

    /// <summary>Usernames who chose to hide this template from their own Templates view
    /// (it was shared with them, but they don't want it cluttering their list). Doesn't
    /// revoke the share - the owner/Admin still see it shared, this just hides it for them.</summary>
    public List<string> HiddenByUsernames { get; set; } = new();

    /// <summary>
    /// Columns whose raw value is a fixed-width date/time string (e.g. "20260809220911")
    /// that should display reformatted (e.g. "2026/08/09 22:09:11") in the result grid.
    /// Purely a display setting - the underlying query and data are untouched.
    /// </summary>
    public List<DateFieldFormat> DateFieldFormats { get; set; } = new();

    // ---- Workflow (see WorkflowDefinition). Empty WorkflowSteps = not in a workflow. ----
    public Guid? WorkflowDefinitionId { get; set; }
    public string? WorkflowName { get; set; }

    /// <summary>This template's own copy of the steps, taken when the workflow started.</summary>
    public List<WorkflowStep> WorkflowSteps { get; set; } = new();
    public int WorkflowStepIndex { get; set; }
}

/// <summary>
/// SourceFormat/DisplayFormat use .NET custom date format tokens (yyyy, MM, dd, HH, mm, ss)
/// applied positionally - e.g. SourceFormat "yyyyMMddHHmmss" parses "20260809220911" by
/// taking 4 digits for the year, 2 for the month, and so on in that order, regardless of
/// separators (there usually aren't any in these raw formats). DisplayFormat controls how
/// those parsed parts are rendered back out, e.g. "yyyy/MM/dd HH:mm:ss".
/// </summary>
public class DateFieldFormat
{
    public string ColumnName { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = "yyyyMMddHHmmss";
    public string DisplayFormat { get; set; } = "yyyy/MM/dd HH:mm:ss";
}
