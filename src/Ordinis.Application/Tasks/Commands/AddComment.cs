using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Posts a new comment on a task.
/// </summary>
/// <param name="TaskId">The ID of the task to comment on.</param>
/// <param name="AuthorId">The ID of the user authoring the comment.</param>
/// <param name="Content">Comment text (max 10 000 characters).</param>
public sealed record AddComment(
    Guid TaskId,
    Guid AuthorId,
    string Content) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="AddComment"/> by invoking <see cref="ProjectTask.AddComment"/>.
/// and returning the new comment's ID.
/// </summary>
internal sealed class AddCommentHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<AddComment, Guid>
{
    public async Task<Guid> HandleAsync(AddComment command, CancellationToken cancellationToken)
    {
        // Include comments so the aggregate can enforce its invariants
        // (e.g. task must not be deleted) and so EF Core tracks the new comment.
        ProjectTask task = await db.Tasks
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        Comment comment = task.AddComment(
            authorId: command.AuthorId,
            content: command.Content,
            now: timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);

        return comment.Id;
    }
}

// Validator
/// <summary>
/// Validates <see cref="AddComment"/> commands.
/// </summary>
internal sealed class AddCommentValidator : AbstractValidator<AddComment>
{
    public AddCommentValidator()
    {
        RuleFor(c => c.TaskId)
            .NotEmpty();

        RuleFor(c => c.Content)
            .NotEmpty()
            .MaximumLength(10_000);
    }
}
