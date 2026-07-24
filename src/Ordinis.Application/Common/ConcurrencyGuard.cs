namespace Ordinis.Application.Common;

/// <summary>
/// Compares a client-supplied <c>If-Match</c> concurrency token (decoded from the request's
/// <c>If-Match</c> header by <c>ConcurrencyTokenMiddleware</c>) against an aggregate's current
/// <c>RowVersion</c>, proactively detecting a stale client-side copy before any write is
/// attempted.
/// </summary>
/// <remarks>
/// This is distinct from - and in addition to - the reactive <c>DbUpdateConcurrencyException</c>
/// catch already present in every affected command handler: that catch guards against a write
/// race between this request's own load and its own <c>SaveChangesAsync</c>, whereas this guard
/// guards against the client having fetched the resource before some earlier, already-committed
/// change.
/// </remarks>
public static class ConcurrencyGuard
{
    /// <summary>
    /// Throws <see cref="ConcurrencyException"/> if <paramref name="ifMatch"/> does not match
    /// <paramref name="current"/>.
    /// </summary>
    /// <param name="current">The aggregate's current <c>RowVersion</c>.</param>
    /// <param name="ifMatch">
    /// The token decoded from the request's <c>If-Match</c> header. Validation (a
    /// <c>NotNull</c> rule on the owning command) guarantees this is non-null by the time a
    /// handler runs, so any command that has actually been dispatched will have one.
    /// </param>
    /// <param name="entityType">The type name of the aggregate, used in the exception message.</param>
    /// <param name="entityId">The ID of the aggregate.</param>
    public static void EnsureMatch(byte[]? current, byte[]? ifMatch, string entityType, Guid entityId)
    {
        if (ifMatch is null)
        {
            return;
        }

        if (current is null || !current.AsSpan().SequenceEqual(ifMatch))
        {
            throw new ConcurrencyException(entityType, entityId);
        }
    }
}
