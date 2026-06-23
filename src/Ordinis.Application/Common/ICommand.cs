namespace Ordinis.Application.Common;

/// <summary>
/// Marker interface for commands that mutate state and do not return a value.
/// </summary>
/// <remarks>
/// Implement this on a sealed record for each command, e.g.
/// <c>public sealed record MoveTask(Guid TaskId, ProjectTaskStatus NewStatus) : ICommand;</c>
/// Handled by a matching <see cref="ICommandHandler{TCommand}"/>.
/// </remarks>
public interface ICommand
{
}

/// <summary>
/// Marker interface for commands that mutate state and return <typeparamref name="TResult"/>,
/// e.g. a DTO describing the entity that was just created.
/// </summary>
/// <typeparam name="TResult">The type returned after the command is handled.</typeparam>
/// <remarks>
/// Handled by a matching <see cref="ICommandHandler{TCommand, TResult}"/>.
/// </remarks>
public interface ICommand<TResult>
{
}
