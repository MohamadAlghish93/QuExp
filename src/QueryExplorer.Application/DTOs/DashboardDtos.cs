namespace QueryExplorer.Application.DTOs;

public class SaveDashboardItemRequest
{
    public string Title { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }
    public string GroupByColumn { get; set; } = string.Empty;
}

public class ShareDashboardItemRequest
{
    public string Username { get; set; } = string.Empty;
}

public class DashboardItemResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string ConnectionName { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string GroupByColumn { get; set; } = string.Empty;

    public string OwnerUsername { get; set; } = string.Empty;
    public bool IsOwnedByMe { get; set; }
    public List<string> SharedWithUsernames { get; set; } = new();

    // Live data, re-fetched each time the dashboard loads.
    public List<GroupByRow>? Groups { get; set; }
    public string? Error { get; set; }
}
