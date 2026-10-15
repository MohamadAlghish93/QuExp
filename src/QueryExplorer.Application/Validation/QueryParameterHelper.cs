using System.Text.RegularExpressions;

namespace QueryExplorer.Application.Validation;

/// <summary>
/// Favorite queries can contain placeholders like {{customer_id}} or {{start_date}}.
/// When a favorite with placeholders is run, the UI collects a value for each one
/// and the API binds them as real database parameters (never by string-concatenating
/// into the SQL text), so parameter values can't be used to break out of the query.
/// </summary>
public static class QueryParameterHelper
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{\s*(\w+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>Distinct placeholder names, in first-occurrence order.</summary>
    public static List<string> ExtractParameterNames(string sql)
    {
        var names = new List<string>();
        foreach (Match match in PlaceholderRegex.Matches(sql))
        {
            var name = match.Groups[1].Value;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }
        return names;
    }

    /// <summary>Replaces every {{name}} with the provider's native bind-variable marker (e.g. "@name" or ":name").</summary>
    public static string RenderForProvider(string sql, string markerPrefix)
        => PlaceholderRegex.Replace(sql, m => $"{markerPrefix}{m.Groups[1].Value}");
}
