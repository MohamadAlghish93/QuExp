namespace QueryExplorer.Application.Interfaces;

public interface IPushNotificationSender
{
    /// <summary>
    /// Sends a Web Push notification to every subscription the user has registered.
    /// Best-effort: a dead/expired subscription is removed rather than thrown as an error,
    /// and a delivery failure never bubbles up to interrupt whatever triggered it (an alert
    /// firing, an assignment reminder, etc).
    /// </summary>
    Task SendToUserAsync(string username, string title, string body, string? url = null);
}
