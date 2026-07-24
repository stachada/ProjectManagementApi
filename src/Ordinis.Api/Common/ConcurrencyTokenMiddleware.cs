namespace Ordinis.Api.Common;

/// <summary>
/// Extracts the <c>If-Match</c> request header, decodes it from Base64, and exposes the
/// resulting <c>byte[]</c> on <see cref="HttpContext.Items"/> for controllers to read and pass
/// into commands as the <c>IfMatch</c> concurrency token.
/// </summary>
/// <remarks>
/// This middleware only extracts and decodes - it does not decide which endpoints require
/// <c>If-Match</c>, nor does it reject requests. Both an absent header and a malformed one
/// leave <see cref="HttpContext.Items"/> unset, which command validators surface uniformly as
/// a <c>422</c> "If-Match is required" error via the existing <c>IValidator&lt;T&gt;</c>
/// pipeline - keeping this middleware a dumb, endpoint-agnostic extractor.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class ConcurrencyTokenMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The <see cref="HttpContext.Items"/> key under which the decoded <c>If-Match</c> token is
    /// stored, when present and well-formed.
    /// </summary>
    public const string ItemsKey = "IfMatchToken";

    private readonly RequestDelegate _next = next;

    /// <summary>
    /// Decodes the <c>If-Match</c> header, if present, and stashes it on
    /// <see cref="HttpContext.Items"/> before continuing the pipeline.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("If-Match", out Microsoft.Extensions.Primitives.StringValues headerValue))
        {
            string token = headerValue.ToString().Trim();

            // Real ETags are quoted (and may carry a weak-validator "W/" prefix); strip both
            // so the remaining content is the raw Base64 payload produced by TaskMapper et al.
            if (token.StartsWith("W/", StringComparison.Ordinal))
            {
                token = token[2..];
            }

            if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
            {
                token = token[1..^1];
            }

            try
            {
                context.Items[ItemsKey] = Convert.FromBase64String(token);
            }
            catch (FormatException)
            {
                // Malformed If-Match value - leave unset, treated the same as a missing header.
            }
        }

        await _next(context);
    }
}
