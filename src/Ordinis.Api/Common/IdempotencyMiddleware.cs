using System.Security.Cryptography;
using Microsoft.Extensions.Primitives;
using Ordinis.Application.Common;

namespace Ordinis.Api.Common;

/// <summary>
/// Reads the <c>Idempotency-Key</c> request header on <see cref="IdempotentAttribute"/>-marked
/// <c>POST</c> actions, caches the resulting response, and replays it verbatim if the same key
/// is sent again with the same request body.
/// </summary>
/// <remarks>
/// A missing header, a non-<c>POST</c> request, or a route with no <see cref="IdempotentAttribute"/>
/// all leave this middleware a no-op pass-through — same "dumb, endpoint-agnostic extractor"
/// convention as <see cref="ConcurrencyTokenMiddleware"/>. Only <c>2xx</c> responses are cached;
/// a failed request (validation error, 404, 500, ...) is never cached, so the client can retry
/// the same key once the request is fixed.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="store">The backing store for cached responses.</param>
public sealed class IdempotencyMiddleware(RequestDelegate next, IIdempotencyStore store)
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly RequestDelegate _next = next;
    private readonly IIdempotencyStore _store = store;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null ||
            !context.Request.Headers.TryGetValue(HeaderName, out StringValues headerValue) ||
            string.IsNullOrWhiteSpace(headerValue.ToString()))
        {
            await _next(context);
            return;
        }

        string idempotencyKey = headerValue.ToString().Trim();
        string cacheKey = $"{context.Request.Method}:{context.Request.Path}:{idempotencyKey}";
        string requestBodyHash = await HashRequestBodyAsync(context);

        IdempotencyRecord? cached = await _store.TryGetAsync(cacheKey, context.RequestAborted);
        if (cached is not null)
        {
            if (!string.Equals(cached.RequestBodyHash, requestBodyHash, StringComparison.Ordinal))
            {
                throw new IdempotencyKeyConflictException(idempotencyKey);
            }

            await ReplayAsync(context, cached);
            return;
        }

        Stream originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        byte[] bodyBytes = buffer.ToArray();

        if (context.Response.StatusCode < 300)
        {
            string? location = context.Response.Headers.TryGetValue("Location", out StringValues locationValue)
                ? locationValue.ToString()
                : null;

            var record = new IdempotencyRecord(
                requestBodyHash,
                context.Response.StatusCode,
                context.Response.ContentType,
                location,
                bodyBytes);

            await _store.SetAsync(cacheKey, record, CacheTtl, context.RequestAborted);
        }

        await originalBody.WriteAsync(bodyBytes, context.RequestAborted);
    }

    private static async Task<string> HashRequestBodyAsync(HttpContext context)
    {
        // Read raw bytes rather than decoding as text — one in-scope route (AddAttachment)
        // sends a multipart/binary body, and a decode-then-re-encode round trip through UTF-8
        // would silently corrupt invalid byte sequences before hashing.
        context.Request.EnableBuffering();
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        context.Request.Body.Position = 0;

        byte[] hashBytes = SHA256.HashData(buffer.ToArray());
        return Convert.ToHexStringLower(hashBytes);
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord cached)
    {
        context.Response.StatusCode = cached.StatusCode;
        if (cached.ContentType is not null)
        {
            context.Response.ContentType = cached.ContentType;
        }

        if (cached.Location is not null)
        {
            context.Response.Headers["Location"] = cached.Location;
        }

        await context.Response.Body.WriteAsync(cached.Body, context.RequestAborted);
    }
}
