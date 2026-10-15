using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueryExplorer.Application.DTOs;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Application.Validation;
using QueryExplorer.Domain.Entities;
using QueryExplorer.Infrastructure.Export;

namespace QueryExplorer.Api.Controllers;

[ApiController]
[Route("api/query")]
[Authorize]
public class QueryController : ControllerBase
{
    private readonly IConnectionRepository _connections;
    private readonly IQueryProviderFactory _providerFactory;
    private readonly IConnectionStringResolver _resolver;
    private readonly IQueryHistoryRepository _history;

    public QueryController(
        IConnectionRepository connections,
        IQueryProviderFactory providerFactory,
        IConnectionStringResolver resolver,
        IQueryHistoryRepository history)
    {
        _connections = connections;
        _providerFactory = providerFactory;
        _resolver = resolver;
        _history = history;
    }

    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";

    /// <summary>
    /// Returns one page of results. The client calls this repeatedly (grid scroll / page change)
    /// rather than the server ever materializing the full result set.
    /// </summary>
    [HttpPost("page")]
    public async Task<ActionResult<QueryResultPage>> RunPage([FromBody] QueryRequest request, CancellationToken ct)
    {
        var (isValid, error) = SqlValidator.Validate(request.Sql);
        if (!isValid) return BadRequest(new { error });

        var requiredParams = QueryParameterHelper.ExtractParameterNames(request.Sql);
        var missing = requiredParams.Where(p => request.Parameters is null || !request.Parameters.ContainsKey(p)).ToList();
        if (missing.Count > 0)
            return BadRequest(new { error = "Missing parameter value(s).", missingParameters = missing });

        var connection = await _connections.GetByIdAsync(request.ConnectionId);
        if (connection is null) return NotFound(new { error = "Unknown connection id." });
        if (!connection.CanAccess(Username, Role)) return Forbid();

        var provider = _providerFactory.GetProvider(connection.ProviderType);
        var take = Math.Clamp(request.Take, 1, 1000); // hard ceiling protects the server regardless of client input
        var parameters = (request.Parameters ?? new Dictionary<string, string>())
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var connectionString = _resolver.Resolve(connection);

        try
        {
            // Fetch one extra row to know if there's a next page, without running a separate COUNT.
            var (columns, rows) = await provider.ExecutePageAsync(
                connectionString, request.Sql, request.Skip, take + 1, parameters, ct);

            var hasMore = rows.Count > take;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            // Row count runs in parallel and is allowed to fail/time out silently - see provider implementation.
            var countTask = provider.TryGetEstimatedRowCountAsync(connectionString, request.Sql, parameters, ct);
            var estimatedCount = request.Skip == 0 ? await countTask : null;

            stopwatch.Stop();

            // Only log the initial run (skip == 0), not every page/scroll - history should
            // reflect "queries the user ran", not every fetch that run produced.
            if (request.Skip == 0)
            {
                await LogHistoryAsync(connection, request, true, null,
                    estimatedCount ?? rows.Count, stopwatch.ElapsedMilliseconds);
            }

            return Ok(new QueryResultPage
            {
                Columns = columns,
                Rows = rows,
                Skip = request.Skip,
                Take = take,
                HasMore = hasMore,
                EstimatedTotalCount = estimatedCount,
                ElapsedMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex) when (request.Skip == 0)
        {
            stopwatch.Stop();
            await LogHistoryAsync(connection, request, false, ex.Message, null, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task LogHistoryAsync(
        Domain.Entities.DbConnectionProfile connection, QueryRequest request,
        bool success, string? error, long? rowCount, long durationMs)
    {
        try
        {
            await _history.AddAsync(new QueryHistoryEntry
            {
                Username = Username,
                ConnectionId = connection.Id,
                ConnectionName = connection.Name,
                Sql = request.Sql,
                ParametersJson = request.Parameters is { Count: > 0 } ? JsonSerializer.Serialize(request.Parameters) : null,
                Success = success,
                Error = error,
                RowCount = rowCount,
                DurationMs = durationMs
            });
        }
        catch
        {
            // History is a convenience feature - never let a logging failure break the actual query.
        }
    }

    /// <summary>
    /// Runs GROUP BY + COUNT(*) over the ENTIRE result set of the query - not just the
    /// page currently loaded in the grid - so counts are accurate for large results.
    /// </summary>
    [HttpPost("groupby")]
    public async Task<ActionResult<GroupByResult>> GroupBy([FromBody] GroupByRequest request, CancellationToken ct)
    {
        var (isValid, error) = SqlValidator.Validate(request.Sql);
        if (!isValid) return BadRequest(new { error });

        if (!IdentifierValidator.IsSafe(request.Column))
            return BadRequest(new { error = "Invalid column name." });

        var requiredParams = QueryParameterHelper.ExtractParameterNames(request.Sql);
        var missing = requiredParams.Where(p => request.Parameters is null || !request.Parameters.ContainsKey(p)).ToList();
        if (missing.Count > 0)
            return BadRequest(new { error = "Missing parameter value(s).", missingParameters = missing });

        var connection = await _connections.GetByIdAsync(request.ConnectionId);
        if (connection is null) return NotFound(new { error = "Unknown connection id." });
        if (!connection.CanAccess(Username, Role)) return Forbid();

        var provider = _providerFactory.GetProvider(connection.ProviderType);
        var top = Math.Clamp(request.Top, 1, 1000);
        var parameters = (request.Parameters ?? new Dictionary<string, string>())
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Ask for one extra group so we can tell the user the list was cut off.
        var groups = await provider.ExecuteGroupByAsync(
            _resolver.Resolve(connection), request.Sql, request.Column, top + 1, parameters, ct);

        stopwatch.Stop();

        var truncated = groups.Count > top;
        if (truncated) groups.RemoveAt(groups.Count - 1);

        return Ok(new GroupByResult
        {
            Column = request.Column,
            Groups = groups.Select(g => new GroupByRow { Value = g.Value, Count = g.Count }).ToList(),
            Truncated = truncated,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        });
    }

    /// <summary>
    /// Streams the full (unpaged) result set to the response as CSV so the browser
    /// never has to hold a million rows in memory, and neither does the server.
    /// </summary>
    [HttpGet("export")]
    public async Task Export(
        [FromQuery] Guid connectionId, [FromQuery] string sql, [FromQuery] string? parameters, CancellationToken ct)
    {
        var (isValid, error) = SqlValidator.Validate(sql);
        if (!isValid)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error }, ct);
            return;
        }

        Dictionary<string, string> paramValues;
        try
        {
            paramValues = string.IsNullOrWhiteSpace(parameters)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(parameters) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "parameters must be a JSON object of name/value pairs." }, ct);
            return;
        }

        var requiredParams = QueryParameterHelper.ExtractParameterNames(sql);
        var missing = requiredParams.Where(p => !paramValues.ContainsKey(p)).ToList();
        if (missing.Count > 0)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "Missing parameter value(s).", missingParameters = missing }, ct);
            return;
        }

        var connection = await _connections.GetByIdAsync(connectionId);
        if (connection is null)
        {
            Response.StatusCode = 404;
            return;
        }
        if (!connection.CanAccess(Username, Role))
        {
            Response.StatusCode = 403;
            return;
        }

        var provider = _providerFactory.GetProvider(connection.ProviderType);
        var boundParams = paramValues.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        Response.ContentType = "text/csv";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=query-result-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

        await CsvExportWriter.WriteAsync(Response.Body, provider, _resolver.Resolve(connection), sql, boundParams, ct);
    }
}
