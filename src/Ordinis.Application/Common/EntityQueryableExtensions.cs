using Ordinis.Domain.Common;

namespace Ordinis.Application.Common;

/// <summary>
/// Shared deterministic tiebreaker for <c>OrderBy</c>/<c>OrderByDescending</c> chains used by
/// paginated queries, capped embedded lists, and DTO-embedding mappers.
/// </summary>
/// <remarks>
/// SQL provides no guaranteed row order for ties on the primary sort key, so without an explicit
/// tiebreaker, which rows land on which page - or inside vs. just outside an embed cap - can vary
/// between otherwise-identical requests. Ending every such ordering with
/// <see cref="ThenByStableId{T}(IOrderedQueryable{T})"/> (or the <see cref="IOrderedEnumerable{T}"/>
/// overload for in-memory mappers) makes the ordering fully deterministic.
/// </remarks>
public static class EntityQueryableExtensions
{
    /// <summary>
    /// Appends a deterministic <c>ThenBy(Id)</c> tiebreaker to an EF Core query's ordering.
    /// </summary>
    public static IOrderedQueryable<T> ThenByStableId<T>(this IOrderedQueryable<T> source)
        where T : Entity
        => source.ThenBy(e => e.Id);

    /// <summary>
    /// In-memory equivalent of <see cref="ThenByStableId{T}(IOrderedQueryable{T})"/>, for mappers
    /// ordering an already-materialized collection.
    /// </summary>
    public static IOrderedEnumerable<T> ThenByStableId<T>(this IOrderedEnumerable<T> source)
        where T : Entity
        => source.ThenBy(e => e.Id);
}
