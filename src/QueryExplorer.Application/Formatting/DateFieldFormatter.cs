using System.Text.RegularExpressions;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Formatting;

/// <summary>
/// Server-side counterpart to the client's dateFieldFormat.js - applies a template's saved
/// DateFieldFormats to raw row values before they're drawn into a PDF, so a formatted date
/// shows formatted there too, not just in the browser grid. Parses a fixed-width positional
/// date (e.g. "20260809220911") using SourceFormat tokens (yyyy, MM, dd, HH, mm, ss) and
/// re-renders using DisplayFormat. Values that don't match the shape are returned unchanged.
/// </summary>
public static class DateFieldFormatter
{
    private static readonly Regex TokenRegex = new("yyyy|yy|MM|dd|HH|mm|ss", RegexOptions.Compiled);

    public static Dictionary<string, object?> ApplyFormats(
        IReadOnlyDictionary<string, object?> rowData, IEnumerable<DateFieldFormat> formats)
    {
        var result = new Dictionary<string, object?>(rowData);
        foreach (var format in formats)
        {
            if (!result.TryGetValue(format.ColumnName, out var raw) || raw is null) continue;
            var formatted = Format(raw.ToString(), format.SourceFormat, format.DisplayFormat);
            if (formatted is not null) result[format.ColumnName] = formatted;
        }
        return result;
    }

    public static string? Format(string? raw, string sourceFormat, string displayFormat)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var tokens = TokenRegex.Matches(sourceFormat).Select(m => m.Value).ToList();
        if (tokens.Count == 0) return raw;

        int? year = null, month = null, day = null;
        int hour = 0, minute = 0, second = 0;
        var index = 0;

        foreach (var token in tokens)
        {
            var len = token.Length == 4 ? 4 : 2;
            if (index + len > raw.Length) return raw;
            var chunk = raw.Substring(index, len);
            index += len;
            if (!chunk.All(char.IsDigit)) return raw;

            var value = int.Parse(chunk);
            switch (token)
            {
                case "yyyy": year = value; break;
                case "yy": year = 2000 + value; break;
                case "MM": month = value; break;
                case "dd": day = value; break;
                case "HH": hour = value; break;
                case "mm": minute = value; break;
                case "ss": second = value; break;
            }
        }

        if (year is null || month is null || day is null) return raw;

        return TokenRegex.Replace(displayFormat, m => m.Value switch
        {
            "yyyy" => year.Value.ToString("D4"),
            "yy" => (year.Value % 100).ToString("D2"),
            "MM" => month.Value.ToString("D2"),
            "dd" => day.Value.ToString("D2"),
            "HH" => hour.ToString("D2"),
            "mm" => minute.ToString("D2"),
            "ss" => second.ToString("D2"),
            _ => m.Value
        });
    }
}
