using System.Diagnostics;

namespace Ordinis.Api.Common;

/// <summary>
/// Logs method, path, status code, elapsed time, and correlation ID for every request at
/// <see cref="LogLevel.Information"/>.
/// </summary>
/// <remarks>
/// Registered after <see cref="CorrelationIdMiddleware"/> and before <see cref="GlobalExceptionMiddleware"/>,
/// so the exception middleware has already translated any thrown exception into a Problem Details
/// response by the time this middleware observes <c>context.Response.StatusCode</c>.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="RequestLoggingMiddleware"/> class.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="logger">Logger used to emit the per-request summary line.</param>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RequestLoggingMiddleware> _logger = logger;

    /// <summary>
    /// Times the request and logs a single structured summary line once it completes.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            object? correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out object? id) ? id : null;

            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms (CorrelationId: {CorrelationId})",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                correlationId);
        }
    }
}
