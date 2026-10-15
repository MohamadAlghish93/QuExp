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

    /// <summary>Freeform grouping label (e.g. "Sales", "Finance") for organizing users on the Users page.
    /// Not a permission boundary by itself - just organization/display.</summary>
    public string? GroupName { get; set; }

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
