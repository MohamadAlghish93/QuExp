using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

/// <summary>Backs the browser's PushManager subscription flow - see /public/service-worker.js on the client.</summary>
[ApiController]
[Route("api/push")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly IPushSubscriptionRepository _subscriptions;
    private readonly IConfiguration _config;

    public PushController(IPushSubscriptionRepository subscriptions, IConfiguration config)
    {
        _subscriptions = subscriptions;
        _config = config;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    /// <summary>Public key the browser needs to create a subscription. Empty string means push isn't configured server-side.</summary>
    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    public IActionResult GetVapidPublicKey() => Ok(new { publicKey = _config["Push:VapidPublicKey"] ?? "" });

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribePushRequest request)
    {
        await _subscriptions.AddAsync(new PushSubscription
        {
            Username = Username,
            Endpoint = request.Endpoint,
            P256dh = request.P256dh,
            Auth = request.Auth
        });
        return NoContent();
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribePushRequest request)
    {
        await _subscriptions.RemoveAsync(Username, request.Endpoint);
        return NoContent();
    }
}
