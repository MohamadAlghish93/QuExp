namespace QueryExplorer.Domain.Entities;

/// <summary>
/// A browser's Web Push subscription (from the PushManager API), so the server can
/// send OS-level notifications even when the tab/browser isn't focused. One user can
/// have several (one per browser/device they've enabled notifications on).
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
