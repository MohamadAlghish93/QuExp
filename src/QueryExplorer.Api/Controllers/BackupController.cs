using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api.Controllers;

/// <summary>
/// Export/import everything (minus connection passwords, which never leave the
/// environment they were encrypted in) for migrating between environments or
/// taking a manual backup. Admin-only - this touches every user's data.
/// </summary>
[ApiController]
[Route("api/admin/backup")]
[Authorize(Roles = "Admin")]
public class BackupController : ControllerBase
{
    private readonly IConnectionRepository _connections;
    private readonly IFavoriteQueryRepository _favorites;
    private readonly IAlertRepository _alerts;
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _auditLog;

    public BackupController(
        IConnectionRepository connections, IFavoriteQueryRepository favorites,
        IAlertRepository alerts, IUserRepository users, IAuditLogRepository auditLog)
    {
        _connections = connections;
        _favorites = favorites;
        _alerts = alerts;
        _users = users;
        _auditLog = auditLog;
    }

    private string ActorUsername => HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "unknown";

    [HttpGet("export")]
    public async Task<ActionResult<BackupBundle>> Export()
    {
        var connections = await _connections.GetAllAsync();
        foreach (var c in connections) c.EncryptedPassword = string.Empty; // never leaves this environment

        var favorites = await _favorites.GetAllAsync();

        // Alerts don't have a "get all" - pull per-user across everyone with an account.
        var users = await _users.GetAllAsync();
        var allAlerts = new List<QueryAlert>();
        foreach (var u in users)
            allAlerts.AddRange(await _alerts.GetForUserAsync(u.Username));

        var bundle = new BackupBundle
        {
            Connections = connections,
            Favorites = favorites,
            Alerts = allAlerts,
            Users = users
        };

        await _auditLog.AddAsync(new AuditLogEntry
        {
            ActorUsername = ActorUsername,
            Action = "DataExported",
            TargetDescription = $"{connections.Count} connections, {favorites.Count} favorites, {allAlerts.Count} alerts, {users.Count} users"
        });

        return Ok(bundle);
    }

    /// <summary>
    /// Imports a bundle. Connections come back with no password (as exported) -
    /// re-enter and test each one after restoring. Existing records are matched by
    /// Id/Username and overwritten; anything not in the bundle is left untouched
    /// (this never deletes data the target already has).
    /// </summary>
    [HttpPost("restore")]
    public async Task<ActionResult<RestoreResult>> Restore([FromBody] BackupBundle bundle)
    {
        var result = new RestoreResult();

        foreach (var connection in bundle.Connections ?? new List<DbConnectionProfile>())
        {
            connection.EncryptedPassword = string.Empty; // defensive - never trust a password blob from an import file
            await _connections.SaveAsync(connection);
            result.ConnectionsImported++;
        }

        foreach (var favorite in bundle.Favorites ?? new List<FavoriteQuery>())
        {
            await _favorites.SaveAsync(favorite);
            result.FavoritesImported++;
        }

        foreach (var alert in bundle.Alerts ?? new List<QueryAlert>())
        {
            await _alerts.SaveAsync(alert);
            result.AlertsImported++;
        }

        foreach (var user in bundle.Users ?? new List<User>())
        {
            var existing = await _users.GetByUsernameAsync(user.Username);
            if (existing is null)
            {
                await _users.CreateAsync(user); // carries over the original (hashed) password as-is
            }
            else
            {
                await _users.UpdateRoleAsync(user.Username, user.Role);
                await _users.UpdateAllowedScreensAsync(user.Username, user.AllowedScreens);
                await _users.UpdateProfileAsync(user.Username, user.DisplayName, user.Email, user.GroupName);
                await _users.UpdatePasswordAsync(user.Username, user.PasswordHash, user.PasswordSalt);
            }
            result.UsersImported++;
        }

        await _auditLog.AddAsync(new AuditLogEntry
        {
            ActorUsername = ActorUsername,
            Action = "DataRestored",
            TargetDescription = $"{result.ConnectionsImported} connections, {result.FavoritesImported} favorites, {result.AlertsImported} alerts, {result.UsersImported} users"
        });

        return Ok(result);
    }
}
