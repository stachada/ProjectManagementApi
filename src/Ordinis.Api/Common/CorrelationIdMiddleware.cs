namespace Ordinis.Api.Common;

/// <summary>
/// Generates a correlation ID for each request (or propagates one supplied by the caller),
/// attaches it to the response headers and to the <see cref="ILogger"/> scope so every log line
/// written while handling the request carries it.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Name of the request/response header and <see cref="HttpContext.Items"/> key used to
    /// carry the correlation ID.
    /// </summary>
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next = next;

    /// <summary>
    /// Resolves the correlation ID, exposes it on <see cref="HttpContext.Items"/> and the
    /// response header, and pushes it onto the logger scope for the duration of the request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        string correlationId = context.Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues existing)
            && !string.IsNullOrWhiteSpace(existing)
                ? existing.ToString()
                : Guid.CreateVersion7().ToString();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
