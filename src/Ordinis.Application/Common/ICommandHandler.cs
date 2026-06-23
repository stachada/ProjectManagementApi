namespace Ordinis.Application.Common;

/// <summary>
/// Handles a command that mutates state and does not return a value.
/// </summary>
/// <typeparam name="TCommand">The command type this handler processes.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a command that mutates state and returns <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TCommand">The command type this handler processes.</typeparam>
/// <typeparam name="TResult">The type returned after the command is handled.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
