using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/connections")]
[Authorize]
public class ConnectionsController : ControllerBase
{
    private readonly IConnectionRepository _connections;
    private readonly IQueryProviderFactory _providerFactory;
    private readonly ISecretProtector _protector;
    private readonly IConnectionStringResolver _resolver;
    private readonly IConnectionHealthCache _healthCache;

    public ConnectionsController(
        IConnectionRepository connections,
        IQueryProviderFactory providerFactory,
        ISecretProtector protector,
        IConnectionStringResolver resolver,
        IConnectionHealthCache healthCache)
    {
        _connections = connections;
        _providerFactory = providerFactory;
        _protector = protector;
        _resolver = resolver;
        _healthCache = healthCache;
    }

    private string UserKey => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";

    private static ConnectionResponse ToResponse(DbConnectionProfile c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ProviderType = c.ProviderType.ToString(),
        Host = c.Host,
        Port = c.Port,
        Database = c.Database,
        Username = c.Username,
        ExtraOptions = c.ExtraOptions,
        HasPassword = !string.IsNullOrEmpty(c.EncryptedPassword),
        AllowedUsernames = c.AllowedUsernames
        // Note: no password field of any kind is returned to the browser.
    };

    /// <summary>Only returns connections this user is allowed to see - Viewers/Editors restricted
    /// by AllowedUsernames, Admins always see everything.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ConnectionResponse>>> GetAll()
    {
        var all = await _connections.GetAllAsync();
        var visible = all.Where(c => c.CanAccess(UserKey, Role));
        return Ok(visible.Select(ToResponse).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<ActionResult<ConnectionResponse>> Save([FromBody] SaveConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Host))
            return BadRequest(new { error = "Name and host are required." });

        var existing = request.Id.HasValue ? await _connections.GetByIdAsync(request.Id.Value) : null;

        // On edit with a blank password field, keep whatever is already stored
        // rather than wiping it - the browser never receives the old password
        // so it can't send it back.
        string encryptedPassword;
        if (!string.IsNullOrEmpty(request.Password))
            encryptedPassword = _protector.Protect(request.Password);
        else if (existing is not null)
            encryptedPassword = existing.EncryptedPassword;
        else
            encryptedPassword = string.Empty;

        var profile = new DbConnectionProfile
        {
            Id = existing?.Id ?? request.Id ?? Guid.NewGuid(),
            Name = request.Name.Trim(),
            ProviderType = request.ProviderType,
            Host = request.Host.Trim(),
            Port = request.Port,
            Database = request.Database.Trim(),
            Username = request.Username.Trim(),
            EncryptedPassword = encryptedPassword,
            ExtraOptions = request.ExtraOptions,
            AllowedUsernames = request.AllowedUsernames ?? new List<string>(),
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow
        };

        var saved = await _connections.SaveAsync(profile);
        _healthCache.Invalidate(UserKey); // details changed - previous health result is stale
        return Ok(ToResponse(saved));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var removed = await _connections.DeleteAsync(id);
        if (removed) _healthCache.Invalidate(UserKey);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Tests details from the form before saving them, so the user finds out about a
    /// typo immediately. Falls back to the stored password when the form field is blank.
    /// </summary>
    [HttpPost("test")]
    [Authorize(Roles = "Admin,Editor")]
    public async Task<IActionResult> Test([FromBody] SaveConnectionRequest request, CancellationToken ct)
    {
        var password = request.Password;
        if (string.IsNullOrEmpty(password) && request.Id.HasValue)
        {
            var existing = await _connections.GetByIdAsync(request.Id.Value);
            if (existing is not null && !string.IsNullOrEmpty(existing.EncryptedPassword))
                password = _protector.Unprotect(existing.EncryptedPassword);
        }

        var provider = _providerFactory.GetProvider(request.ProviderType);
        var connectionString = provider.BuildConnectionString(
            request.Host, request.Port, request.Database, request.Username, password ?? string.Empty, request.ExtraOptions);

        var (ok, error) = await provider.TestConnectionAsync(connectionString, ct);
        return Ok(new { isReachable = ok, error });
    }

    /// <summary>
    /// Connectivity status for every connection this user can see. Results are cached per
    /// user for the login session, so the dashboard checks once after login instead of on
    /// every page load. Pass refresh=true to force a re-test.
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<List<ConnectionHealthResponse>>> Health(
        [FromQuery] bool refresh = false, CancellationToken ct = default)
    {
        if (!refresh && _healthCache.TryGet(UserKey, out var cached))
            return Ok(cached.Select(ToHealthResponse).ToList());

        var all = await _connections.GetAllAsync();
        var visible = all.Where(c => c.CanAccess(UserKey, Role)).ToList();

        // Test all connections in parallel so a slow/unreachable host doesn't
        // serialize the whole check.
        var tasks = visible.Select(async profile =>
        {
            var provider = _providerFactory.GetProvider(profile.ProviderType);
            try
            {
                var connectionString = _resolver.Resolve(profile);
                var (ok, error) = await provider.TestConnectionAsync(connectionString, ct);
                return new ConnectionHealth(profile.Id, profile.Name, ok, error, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                // e.g. the stored password can't be decrypted because Encryption:Key changed
                return new ConnectionHealth(profile.Id, profile.Name, false, ex.Message, DateTime.UtcNow);
            }
        });

        var results = (await Task.WhenAll(tasks)).ToList();
        _healthCache.Set(UserKey, results);

        return Ok(results.Select(ToHealthResponse).ToList());
    }

    private static ConnectionHealthResponse ToHealthResponse(ConnectionHealth h) => new()
    {
        ConnectionId = h.ConnectionId,
        Name = h.Name,
        IsReachable = h.IsReachable,
        Error = h.Error,
        CheckedAtUtc = h.CheckedAtUtc
    };
}
