using System.IdentityModel.Tokens.Jwt;
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
/// <para>
/// While a keyed request is being processed its key is <i>reserved</i>: an identical request
/// arriving meanwhile gets <c>409</c> "Request in progress" (with <c>Retry-After</c>) instead of
/// executing a second time; retried after completion, it replays the stored response.
/// </para>
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="store">The backing store for cached responses.</param>
public sealed class IdempotencyMiddleware(RequestDelegate next, IIdempotencyStore store)
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a key stays reserved while its request runs. Comfortably longer than any request
    /// should take; it only matters if a request dies without completing or releasing its key.
    /// </summary>
    private static readonly TimeSpan InFlightTtl = TimeSpan.FromMinutes(2);

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
        string callerId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "anonymous";
        string cacheKey = $"{callerId}:{context.Request.Method}:{context.Request.Path}:{idempotencyKey}";
        string requestBodyHash = await HashRequestBodyAsync(context);

        // Atomically claim the key (or learn that someone else holds / held it). Checking the cache
        // and executing as two separate steps let two identical concurrent requests both execute.
        IdempotencyReservation reservation = await _store.TryReserveAsync(
            cacheKey, requestBodyHash, InFlightTtl, context.RequestAborted);

        if (reservation.Status != IdempotencyReservationStatus.Reserved)
        {
            if (!string.Equals(reservation.RequestBodyHash, requestBodyHash, StringComparison.Ordinal))
            {
                throw new IdempotencyKeyConflictException(idempotencyKey);
            }

            if (reservation.Status == IdempotencyReservationStatus.InFlight)
            {
                throw new IdempotencyRequestInProgressException(idempotencyKey);
            }

            await ReplayAsync(context, reservation.Record!);
            return;
        }

        // This request owns the key: it must end in CompleteAsync (2xx) or ReleaseAsync (anything
        // else, including an exception), so a failed attempt never blocks a corrected retry.
        Stream originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        bool completed = false;
        try
        {
            await _next(context);

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

                await _store.CompleteAsync(cacheKey, record, CacheTtl, CancellationToken.None);
                completed = true;
            }

            context.Response.Body = originalBody;
            await originalBody.WriteAsync(bodyBytes, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;

            if (!completed)
            {
                // CancellationToken.None: the release must happen even if the client went away.
                await _store.ReleaseAsync(cacheKey, CancellationToken.None);
            }
        }
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
