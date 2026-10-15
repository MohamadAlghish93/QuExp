namespace QueryExplorer.Domain.Enums;

/// <summary>Only meaningful for templates (IsTemplate = true). Personal favorites are always effectively "Approved".</summary>
public enum TemplateApprovalStatus
{
    Approved,
    Pending,
    Rejected
}
