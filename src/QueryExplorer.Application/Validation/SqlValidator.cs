using System.Text.RegularExpressions;

namespace QueryExplorer.Application.Validation;

/// <summary>
/// Lightweight guard against non-SELECT / multi-statement input.
/// This is a UX safety net, NOT a security boundary — the real boundary is
/// running queries through a read-only database account/role.
/// </summary>
public static class SqlValidator
{
    private static readonly string[] ForbiddenKeywords =
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "MERGE",
        "EXEC", "EXECUTE", "CREATE", "GRANT", "REVOKE", "CALL"
    };

    public static (bool IsValid, string? Error) Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "Query cannot be empty.");

        var trimmed = sql.Trim().TrimEnd(';');

        if (trimmed.Contains(';'))
            return (false, "Only a single statement is allowed.");

        if (!Regex.IsMatch(trimmed, @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            return (false, "Only SELECT statements (optionally starting with WITH) are allowed.");

        foreach (var keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(trimmed, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
                return (false, $"Statement contains a forbidden keyword: {keyword}.");
        }

        return (true, null);
    }
}
