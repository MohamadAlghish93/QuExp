using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash, base64-encoded. Never store or return the plain password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Random per-user salt, base64-encoded.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Viewer;

    /// <summary>Optional human-friendly name shown instead of the username where space allows.</summary>
    public string? DisplayName { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// Groups this user belongs to (e.g. "Sales", "Finance") - a user can be in several. Used to organize
    /// the Users page and to decide who is responsible for a workflow step.
    /// </summary>
    public List<string> Groups { get; set; } = new();

    /// <summary>
    /// Which top-nav screens this user can see, e.g. ["dashboard","query","favorites"].
    /// Empty/null means "all screens" (the default, so existing users aren't suddenly
    /// locked out when this feature is introduced). Ignored for Admins, who always see
    /// everything - otherwise an Admin could accidentally lock themselves out of the
    /// page that fixes this. Distinct from Role: Role controls what actions a user can
    /// take (edit vs view), this controls which screens even appear in their menu.
    /// </summary>
    public List<string> AllowedScreens { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
