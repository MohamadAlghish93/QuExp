using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QueryExplorer.Application.Interfaces;
using WebPush;

namespace QueryExplorer.Infrastructure.Services;

/// <summary>
/// Sends real OS-level browser notifications via the Web Push protocol (RFC 8030) -
/// these arrive even if the app's tab isn't open, as long as the browser is running
/// (and on many OSes, even if it isn't). Uses VAPID keys configured in appsettings.json
/// (see the "Push" section) so the browser's push service can verify the sender.
/// </summary>
public class WebPushNotificationSender : IPushNotificationSender
{
    private readonly IPushSubscriptionRepository _subscriptions;
    private readonly ILogger<WebPushNotificationSender> _logger;
    private readonly VapidDetails _vapidDetails;
    private readonly bool _configured;

    public WebPushNotificationSender(
        IPushSubscriptionRepository subscriptions, IConfiguration config, ILogger<WebPushNotificationSender> logger)
    {
        _subscriptions = subscriptions;
        _logger = logger;

        var publicKey = config["Push:VapidPublicKey"];
        var privateKey = config["Push:VapidPrivateKey"];
        var subject = config["Push:VapidSubject"] ?? "mailto:admin@example.com";

        _configured = !string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey);
        _vapidDetails = _configured ? new VapidDetails(subject, publicKey, privateKey) : null!;

        if (!_configured)
            _logger.LogWarning("Push:VapidPublicKey/VapidPrivateKey are not configured - browser push notifications are disabled. " +
                "In-app notifications (the bell icon) still work regardless.");
    }

    public async Task SendToUserAsync(string username, string title, string body, string? url = null)
    {
        if (!_configured) return; // silently no-op - in-app notification already covers this

        var subs = await _subscriptions.GetForUserAsync(username);
        if (subs.Count == 0) return;

        var client = new WebPushClient();
        var payload = System.Text.Json.JsonSerializer.Serialize(new { title, body, url });

        foreach (var sub in subs)
        {
            try
            {
                var pushSubscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await client.SendNotificationAsync(pushSubscription, payload, _vapidDetails);
            }
            catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
            {
                // The browser/OS has invalidated this subscription (uninstalled, permission revoked, etc.) - clean it up.
                await _subscriptions.RemoveByEndpointAsync(sub.Endpoint);
            }
            catch (Exception ex)
            {
                // Push delivery is best-effort; never let it break the caller (alert firing, assignment reminder, etc).
                _logger.LogWarning(ex, "Push notification failed for {Username}", username);
            }
        }
    }
}
