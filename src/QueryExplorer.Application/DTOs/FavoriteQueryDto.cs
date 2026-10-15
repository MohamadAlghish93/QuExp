using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.DTOs;

public class SaveFavoriteRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Category { get; set; } = "Uncategorized";
    public List<string> Tags { get; set; } = new();

    /// <summary>Requesting a template requires Editor/Admin role - enforced server-side, not just hidden client-side.</summary>
    public bool IsTemplate { get; set; }
    public string? Description { get; set; }

    /// <summary>Only used when IsTemplate is true. Empty = share with everyone; otherwise, only these usernames.</summary>
    public List<string>? SharedWithUsernames { get; set; }

    public List<DateFieldFormat>? DateFieldFormats { get; set; }
}

public class RenameCategoryRequest
{
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}

public class RenameTagRequest
{
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}

public class BulkCategoryRequest
{
    public List<Guid> Ids { get; set; } = new();
    public string Category { get; set; } = string.Empty;
}

public class BulkTagRequest
{
    public List<Guid> Ids { get; set; } = new();
    public string Tag { get; set; } = string.Empty;
}

public class BulkDeleteRequest
{
    public List<Guid> Ids { get; set; } = new();
}
