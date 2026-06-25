using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Updates the scalar properties of an existing task:
/// <see cref="Title"/>, <see cref="Description"/>, <see cref="Priority"/>, and <see cref="DueDate"/>.
/// </summary>
/// <remarks>
/// Status transitions and assignment changes are handled by dedicated commands
/// (<see cref="MoveTask"/>, <see cref="AssignTask"/>, <see cref="UnassignTask"/>)
/// rather than through this general-purpose update - keeping each command's
/// intent clear and its validation focused.
/// </remarks>
/// <param name="TaskId">ID of the task to update.</param>
/// <param name="Title">New title (max 200 characters).</param>
/// <param name="Description">New description, or <see langword="null"/> to clear it.</param>
/// <param name="Priority">New priority level.</param>
/// <param name="DueDate">New due date, or <see langword="null"/> to clear it.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
public sealed record UpdateTask(
    Guid TaskId,
    string Title,
    string? Description,
    Priority Priority,
    DateTimeOffset? DueDate,
    Guid RequestedByUserId
) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UpdateTask"/> by loading the task, applying mutations,
/// and persisting the changes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Optimistic concurrency:</strong> <c>DbUpdateConcurrencyException</c> from EF Core
/// is caught here and translated to <see cref="ConcurrencyException"/> - keeping the API
/// layer free of any EF Core dependency. The exception propagates to the global middleware,
/// which maps it to a <c>409 Conflict</c> Problem Details response.
/// </para>
/// <para>
/// The <c>RowVersion</c> concurrency token on <c>ProjectTask</c> is configured via
/// EF Core fluent API. EF Core automatically included it in the
/// <c>WHERE</c> clause of the generated <c>UPDATE</c> statement.
/// </para>
/// </remarks>
/// <param name="db"></param>
/// <param name="timeProvider"></param>
internal sealed class UpdateTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<UpdateTask>
{
    public async Task HandleAsync(UpdateTask command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        DateTimeOffset now = timeProvider.GetUtcNow();

        task.UpdateDetails(command.Title, command.Description);
        task.ChangePriority(command.Priority);
        task.SetDueDate(command.DueDate, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Translate to the application-owned exception type.
            // The global middleware maps ConcurrencyException to -> 409 Conflict.
            throw new ConcurrencyException(
                "Task",
                command.TaskId,
                ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="UpdateTask"/> commands - same field rules as <see cref="CreateTaskValidator"/>.
/// </summary>
internal sealed class UpdateTaskValidator : AbstractValidator<UpdateTask>
{
    public UpdateTaskValidator()
    {
        RuleFor(t => t.TaskId)
            .NotEmpty();

        RuleFor(t => t.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(t => t.Priority)
            .IsInEnum()
            .WithMessage("Priority must be a valid Priority value.");

        RuleFor(t => t.RequestedByUserId)
            .NotEmpty();
    }
}
