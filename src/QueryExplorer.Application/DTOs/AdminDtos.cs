using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Application.DTOs;

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Viewer;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? GroupName { get; set; }
}

public class UpdateUserProfileRequest
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? GroupName { get; set; }
}

public class UpdateUserRoleRequest
{
    public UserRole Role { get; set; }
}

public class UpdateUserPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class UpdateUserScreensRequest
{
    /// <summary>Empty list means "all screens" (see User.AllowedScreens doc comment).</summary>
    public List<string> Screens { get; set; } = new();
}

public class UserSummaryResponse
{
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> AllowedScreens { get; set; } = new();
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? GroupName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Minimal, non-sensitive user info - used for the assignee picker, available to any logged-in user.</summary>
public class BasicUserResponse
{
    public string Username { get; set; } = string.Empty;
}
