using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Security;
using QueryExplorer.Domain.Entities;
using System.Security.Claims;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IJwtTokenService _jwt;

    public AuthController(IUserRepository users, IJwtTokenService jwt)
    {
        _users = users;
        _jwt = jwt;
    }

    /// <summary>Public login endpoint. A default admin/Admin123! account is seeded on first run.</summary>
    /// Creating additional users is done from the Users page (Admin role required), not here.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required." });

        var user = await _users.GetByUsernameAsync(request.Username.Trim());
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
            return Unauthorized(new { error = "Invalid username or password." });

        var (token, expiresAtUtc) = _jwt.GenerateToken(user);

        return Ok(new LoginResponse
        {
            Token = token,
            Username = user.Username,
            Role = user.Role.ToString(),
            AllowedScreens = user.AllowedScreens,
            ExpiresAtUtc = expiresAtUtc
        });
    }

    /// <summary>
    /// Returns the caller's current role and allowed screens. Called on app load so a
    /// permission change an Admin makes (e.g. via the Permissions page) takes effect on
    /// the next page load/refresh, without requiring the affected user to log out and back in.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var username = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name);
        if (string.IsNullOrEmpty(username)) return Unauthorized();

        var user = await _users.GetByUsernameAsync(username);
        if (user is null) return Unauthorized();

        return Ok(new { username = user.Username, role = user.Role.ToString(), allowedScreens = user.AllowedScreens });
    }
}
