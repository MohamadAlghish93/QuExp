using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Security;
using QueryExplorer.Domain.Entities;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Api.Controllers;

/// <summary>Admin-only: create accounts and assign roles. This is the "Users" page's backend.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _auditLog;

    public AdminUsersController(IUserRepository users, IAuditLogRepository auditLog)
    {
        _users = users;
        _auditLog = auditLog;
    }

    private string CurrentUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";

    [HttpGet]
    public async Task<ActionResult<List<UserSummaryResponse>>> GetAll()
    {
        var all = await _users.GetAllAsync();
        return Ok(all.Select(u => new UserSummaryResponse
        {
            Username = u.Username,
            Role = u.Role.ToString(),
            AllowedScreens = u.AllowedScreens,
            DisplayName = u.DisplayName,
            Email = u.Email,
            GroupName = u.GroupName,
            CreatedAtUtc = u.CreatedAtUtc
        }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Password.Length < 8)
            return BadRequest(new { error = "Username is required and password must be at least 8 characters." });

        var existing = await _users.GetByUsernameAsync(request.Username.Trim());
        if (existing is not null)
            return Conflict(new { error = "That username is already taken." });

        var (hash, salt) = PasswordHasher.Hash(request.Password);
        var user = await _users.CreateAsync(new User
        {
            Username = request.Username.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = request.Role,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            GroupName = string.IsNullOrWhiteSpace(request.GroupName) ? null : request.GroupName.Trim()
        });

        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = CurrentUsername, Action = "UserCreated", TargetDescription = $"{user.Username} ({user.Role})" });

        return Ok(new UserSummaryResponse
        {
            Username = user.Username, Role = user.Role.ToString(),
            DisplayName = user.DisplayName, Email = user.Email, GroupName = user.GroupName,
            CreatedAtUtc = user.CreatedAtUtc
        });
    }

    /// <summary>Editable profile fields beyond role/password: display name, email, group.</summary>
    [HttpPut("{username}/profile")]
    public async Task<IActionResult> UpdateProfile(string username, [FromBody] UpdateUserProfileRequest request)
    {
        var user = await _users.GetByUsernameAsync(username);
        if (user is null) return NotFound();

        await _users.UpdateProfileAsync(
            username,
            string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            string.IsNullOrWhiteSpace(request.GroupName) ? null : request.GroupName.Trim());

        return NoContent();
    }

    /// <summary>Existing group names, for autocomplete when assigning/creating a group on the Users page.</summary>
    [HttpGet("groups")]
    public async Task<ActionResult<List<string>>> GetGroups() => Ok(await _users.GetDistinctGroupNamesAsync());

    [HttpPut("{username}/role")]
    public async Task<IActionResult> UpdateRole(string username, [FromBody] UpdateUserRoleRequest request)
    {
        var user = await _users.GetByUsernameAsync(username);
        if (user is null) return NotFound();

        // Don't allow demoting the last remaining Admin - that would lock everyone out of user management.
        if (user.Role == UserRole.Admin && request.Role != UserRole.Admin)
        {
            var adminCount = await _users.CountByRoleAsync(UserRole.Admin);
            if (adminCount <= 1)
                return BadRequest(new { error = "Can't remove the last Admin. Promote another user first." });
        }

        await _users.UpdateRoleAsync(username, request.Role);
        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = CurrentUsername, Action = "UserRoleChanged", TargetDescription = $"{username}: {user.Role} -> {request.Role}" });
        return NoContent();
    }

    /// <summary>
    /// Sets which screens appear in this user's nav menu - a dynamic, per-user list
    /// distinct from Role (which controls what actions they can take). Empty = all
    /// screens. Ignored for Admins client-side, since they always see everything.
    /// </summary>
    [HttpPut("{username}/screens")]
    public async Task<IActionResult> UpdateScreens(string username, [FromBody] UpdateUserScreensRequest request)
    {
        var user = await _users.GetByUsernameAsync(username);
        if (user is null) return NotFound();

        await _users.UpdateAllowedScreensAsync(username, request.Screens ?? new List<string>());
        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = CurrentUsername, Action = "UserScreensChanged", TargetDescription = $"{username}: [{string.Join(", ", request.Screens ?? new List<string>())}]" });
        return NoContent();
    }

    /// <summary>Admin-driven password reset - separate from role management, since they're different concerns.</summary>
    [HttpPut("{username}/password")]
    public async Task<IActionResult> UpdatePassword(string username, [FromBody] UpdateUserPasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters." });

        var user = await _users.GetByUsernameAsync(username);
        if (user is null) return NotFound();

        var (hash, salt) = PasswordHasher.Hash(request.NewPassword);
        await _users.UpdatePasswordAsync(username, hash, salt);
        await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = CurrentUsername, Action = "UserPasswordReset", TargetDescription = username });
        return NoContent();
    }

    [HttpDelete("{username}")]
    public async Task<IActionResult> Delete(string username)
    {
        if (username.Equals(CurrentUsername, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "You can't delete your own account while logged in as it." });

        var user = await _users.GetByUsernameAsync(username);
        if (user is null) return NotFound();

        if (user.Role == UserRole.Admin)
        {
            var adminCount = await _users.CountByRoleAsync(UserRole.Admin);
            if (adminCount <= 1)
                return BadRequest(new { error = "Can't delete the last Admin." });
        }

        var removed = await _users.DeleteAsync(username);
        if (removed) await _auditLog.AddAsync(new AuditLogEntry { ActorUsername = CurrentUsername, Action = "UserDeleted", TargetDescription = username });
        return removed ? NoContent() : NotFound();
    }
}
