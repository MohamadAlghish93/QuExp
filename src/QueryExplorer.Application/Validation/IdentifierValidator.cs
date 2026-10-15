using System.Text.RegularExpressions;

namespace QueryExplorer.Application.Validation;

/// <summary>
/// Column names can't be passed as bound parameters - they're part of the SQL
/// structure - so any column name that gets interpolated into generated SQL
/// (e.g. GROUP BY) is validated here and then quoted/bracketed by the provider
/// ([col] for SQL Server, "COL" for Oracle) before use.
///
/// This is deliberately a DENYLIST of characters that could break out of that
/// quoting, not an allowlist restricted to bare-identifier syntax. Real query
/// results routinely have column aliases with spaces or punctuation (e.g.
/// "Total Count", "Order-Id") - the old allowlist rejected those outright,
/// which is what produced "Invalid column name." for anything but a plain
/// single-word column. The actual safety boundary is the provider's bracket/
/// quote escaping, not this check.
/// </summary>
public static class IdentifierValidator
{
    // Anything that could end the provider's quoting/bracketing early, start a new
    // statement, or begin a comment. Control characters are blocked outright.
    private static readonly Regex DangerousCharacters = new(@"[\]""`;\r\n\x00]|--", RegexOptions.Compiled);

    public static bool IsSafe(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return false;
        if (identifier.Length > 128) return false;
        return !DangerousCharacters.IsMatch(identifier);
    }
}
