using System.Text;
using QueryExplorer.Application.Interfaces;

namespace QueryExplorer.Infrastructure.Export;

/// <summary>
/// Streams query results directly to an output stream as CSV, row by row,
/// so memory usage stays flat no matter how many rows the query returns.
/// </summary>
public static class CsvExportWriter
{
    public static async Task WriteAsync(
        Stream destination, Application.Interfaces.IQueryProvider provider, string connectionString, string sql,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        bool headerWritten = false;

        await foreach (var row in provider.StreamAllAsync(connectionString, sql, parameters, ct))
        {
            if (!headerWritten)
            {
                await writer.WriteLineAsync(string.Join(",", row.Keys.Select(EscapeCsv)));
                headerWritten = true;
            }

            await writer.WriteLineAsync(string.Join(",", row.Values.Select(v => EscapeCsv(v?.ToString() ?? ""))));
        }

        if (!headerWritten)
            await writer.WriteLineAsync(""); // empty result set: still return a valid (empty) file

        await writer.FlushAsync();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
