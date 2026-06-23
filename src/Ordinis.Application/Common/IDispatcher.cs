namespace Ordinis.Application.Common;

/// <summary>
/// Resolves and invokes command and query handlers.
/// Runs FluentValidation before invoking command handlers.
/// Controllers and Minimal API endpoints depend on this interface,
/// not on individual handlers directly.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Dispatches a command that produces not result.
    /// Validates the command before invoking the handler.
    /// </summary>
    /// <exception cref="ValidationException">Thrown if validation fails.</exception>
    Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// Dispatches a command that produces typed result.
    /// Validates the command before invoking the handler.
    /// </summary>
    /// <exception cref="ValidationException">Thrown if validation fails.</exception>
    Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;

    /// <summary>
    /// Dispatches a query and returns the result.
    /// Queries are not validated - they are read-only and carry no side effect.
    /// </summary>
    Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>;
}
