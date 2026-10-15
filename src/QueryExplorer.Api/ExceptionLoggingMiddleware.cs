using System.Security.Claims;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Api;

/// <summary>
/// Catches any exception nothing else handled, records it (with stack trace) for admins, and
/// returns a clean JSON error instead of a raw 500 page. Logging itself can never throw out of
/// here - a failure to write the log must not turn one error into two.
/// </summary>
public class ExceptionLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionLoggingMiddleware> _logger;

    public ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IErrorLogRepository errorLog)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The user navigated away / cancelled - not an error worth logging.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            try
            {
                await errorLog.AddAsync(new ErrorLogEntry
                {
                    Username = context.User.FindFirstValue(ClaimTypes.Name) ?? "(anonymous)",
                    Source = "Server",
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value,
                    StatusCode = 500,
                    Message = ex.Message,
                    Details = ex.ToString()
                });
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Failed to write error log entry");
            }

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.Headers["X-Error-Logged"] = "1"; // tells the SPA not to report this again
                await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred. It has been logged for an administrator." });
            }
        }
    }
}
