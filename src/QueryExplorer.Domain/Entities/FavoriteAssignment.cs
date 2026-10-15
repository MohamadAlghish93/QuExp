namespace QueryExplorer.Domain.Entities;

/// <summary>
/// Assigns a favorite/template to a teammate to check on a recurring basis
/// (the "@mention a saved query" feature). AssignmentReminderService creates an
/// in-app (and push) notification when it's due, and again each time it recurs.
/// </summary>
public class FavoriteAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FavoriteId { get; set; }
    public string FavoriteName { get; set; } = string.Empty;

    /// <summary>Snapshot of the query at assignment time, so the assignee can see and run it
    /// directly from the notification/panel without hunting for the original favorite -
    /// and so it still makes sense even if the favorite is later edited or deleted.</summary>
    public string Sql { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }

    public string AssignedByUsername { get; set; } = string.Empty;
    public string AssignedToUsername { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>0 = one-time only (no recurrence). Otherwise, the reminder repeats every N days after completion.</summary>
    public int RepeatIntervalDays { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextDueAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCompletedAtUtc { get; set; }

    /// <summary>Every time this was marked done, oldest first - so "when did this last actually happen"
    /// has a real answer instead of just the single most recent timestamp.</summary>
    public List<DateTime> CompletionHistory { get; set; } = new();

    public bool Active { get; set; } = true;
}
