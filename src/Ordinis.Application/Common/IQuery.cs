namespace Ordinis.Application.Common;

/// <summary>
/// Marker interface for queries that read state and return <typeparamref name="TResult"/>
/// without mutating anything.
/// </summary>
/// <typeparam name="TResult">The type returned after the query is handled.</typeparam>
/// <remarks>
/// Implement this on a sealed record for each query, e.g.
/// <c>public sealed record GetTaskById(Guid TaskId) : IQuery&lt;TaskDto?&gt;;</c>
/// Handled by a matching <see cref="IQueryHandler{TQuery, TResult}"/>.
/// </remarks>
public interface IQuery<TResult>
{
}
