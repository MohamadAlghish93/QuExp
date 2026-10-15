using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;

namespace QueryExplorer.Api.Controllers;

/// <summary>
/// Minimal, non-sensitive user listing available to ANY logged-in user - powers the
/// "assign to" picker on Favorites. Deliberately separate from AdminUsersController,
/// which exposes full account management and requires Admin.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _users;

    public UsersController(IUserRepository users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<List<BasicUserResponse>>> GetAll()
    {
        var all = await _users.GetAllAsync();
        return Ok(all.Select(u => new BasicUserResponse { Username = u.Username }).ToList());
    }
}
