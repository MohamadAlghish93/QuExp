namespace QueryExplorer.Domain.Enums;

/// <summary>
/// Admin: manage users, all connections, everything.
/// Editor: create/edit connections and template favorites, run queries, manage own alerts.
/// Viewer: run queries against connections they're granted, use favorites/templates, can't manage connections or users.
/// </summary>
public enum UserRole
{
    Viewer,
    Editor,
    Admin
}
