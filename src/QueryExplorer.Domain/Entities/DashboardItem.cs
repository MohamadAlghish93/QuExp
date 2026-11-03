namespace QueryExplorer.Domain.Entities;

/// <summary>
/// A pinned query result shown as a small chart on the user's dashboard (the
/// landing page after login). Stores the query config, not a snapshot -
/// the dashboard re-runs the group-by each time it loads so numbers stay current.
/// </summary>
public class DashboardItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string GroupByColumn { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>Usernames this tile has been shared with, so it also shows up on their dashboard.
    /// The tile is still owned/managed by Username - sharing doesn't transfer ownership.</summary>
    public List<string> SharedWithUsernames { get; set; } = new();

    /// <summary>Usernames who chose to hide this tile from their own dashboard. Only meaningful
    /// for non-owners (a recipient hiding a shared tile) - doesn't affect the owner or anyone else
    /// it's shared with, and doesn't revoke the share itself, just that person's visibility of it.</summary>
    public List<string> HiddenByUsernames { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
