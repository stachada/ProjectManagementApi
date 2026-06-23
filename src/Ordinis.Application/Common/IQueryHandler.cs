namespace Ordinis.Application.Common;

/// <summary>
/// Handles a query that reads state and returns <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TQuery">The query type this handler processes.</typeparam>
/// <typeparam name="TResult">The type returned after the query is handled.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>
    /// Executes the query.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
