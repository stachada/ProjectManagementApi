using System.Linq.Expressions;
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

    /// <summary>
    /// Orders by an enum property's <b>declared numeric order</b> (e.g. Priority Low → Critical,
    /// Status Backlog → Cancelled), regardless of how the column is stored.
    /// </summary>
    /// <remarks>
    /// Enums such as <c>ProjectTaskStatus</c> and <c>Priority</c> are persisted as strings
    /// (<c>HasConversion&lt;string&gt;()</c>), so a plain <c>OrderBy(t =&gt; t.Priority)</c> sorts
    /// the stored text alphabetically ("Critical", "High", "Low", "Medium"). This translates to a
    /// <c>CASE WHEN col = 'Low' THEN 0 WHEN col = 'Medium' THEN 1 … END</c> rank instead, built from
    /// <see cref="Enum.GetValues{TEnum}"/>, so it follows the enum automatically if members are
    /// added. Unknown stored values (none should exist) sort last. End the chain with
    /// <see cref="ThenByStableId{T}(IOrderedQueryable{T})"/> like any other paginated ordering.
    /// </remarks>
    public static IOrderedQueryable<T> OrderByEnumValue<T, TEnum>(
        this IQueryable<T> source,
        Expression<Func<T, TEnum>> selector,
        bool descending = false)
        where TEnum : struct, Enum
    {
        Expression<Func<T, int>> rank = EnumRank(selector);
        return descending ? source.OrderByDescending(rank) : source.OrderBy(rank);
    }

    /// <summary>
    /// Builds <c>x =&gt; x.Prop == v0 ? 0 : x.Prop == v1 ? 1 : … : n</c> over the enum's values in
    /// ascending numeric order. Comparisons are made at the enum type (not its underlying integer),
    /// so EF Core converts each constant with the property's value converter and emits
    /// <c>col = 'Done'</c> — an integer comparison would become <c>CAST(col AS int)</c>, which fails
    /// against string storage.
    /// </summary>
    internal static Expression<Func<T, int>> EnumRank<T, TEnum>(Expression<Func<T, TEnum>> selector)
        where TEnum : struct, Enum
    {
        TEnum[] values = Enum.GetValues<TEnum>()
            .OrderBy(v => Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        Expression rank = Expression.Constant(values.Length);

        for (int i = values.Length - 1; i >= 0; i--)
        {
            rank = Expression.Condition(
                Expression.Equal(selector.Body, Expression.Constant(values[i], typeof(TEnum))),
                Expression.Constant(i),
                rank);
        }

        return Expression.Lambda<Func<T, int>>(rank, selector.Parameters);
    }
}
