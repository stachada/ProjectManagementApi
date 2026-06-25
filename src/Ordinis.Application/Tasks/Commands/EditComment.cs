using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Replaces the content of an existing comment.
/// </summary>
/// <param name="TaskId">The ID of the task that owns the comment.</param>
/// <param name="CommentId">The ID of the comment to edit.</param>
/// <param name="NewContent">Replacement text (max 10 000 characters).</param>
/// <param name="RequestedByUserId">Must match the comment's original author.</param>
public sealed record EditComment(
    Guid TaskId,
    Guid CommentId,
    string NewContent,
    Guid RequestedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="EditComment"/> by loading the task with its comments.
/// finding the target comment, and calling <see cref="Comment.UpdateContent"/> directly.
/// </summary>
/// <remarks>
/// <c>UpdateContent</c> lives on <c>Comment</c> itself rather than being proxied
/// through <c>ProjectTask</c> - editing a comment's text is an operation scoped
/// entirely to the comment and carries no aggregate-level invariant that the task
/// needs to enforce. The task is still loaded (and comments included) so EF Core
/// tracks the change within the same unit of work.
/// </remarks>
/// <param name="db"></param>
internal sealed class EditCommentHandler(
    IAppDbContext db) : ICommandHandler<EditComment>
{
    public async Task HandleAsync(EditComment command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        Comment comment = task.Comments.FirstOrDefault(c => c.Id == command.CommentId)
            ?? throw new NotFoundException(nameof(Comment), command.CommentId);

        comment.UpdateContent(command.NewContent);

        await db.SaveChangesAsync(cancellationToken);
    }
}

// Validator
/// <summary>
/// Validates <see cref="EditComment"/> commands.
/// </summary>
/// <remarks>
/// Author ownership (the requesting user must be the comment author) is verified
/// here via a database lookup rather than loading the full task aggregate - the
/// check is a simple predicate query that does not need aggregate invariants.
/// </remarks>
internal sealed class EditCommentValidator : AbstractValidator<EditComment>
{
    public EditCommentValidator(IAppDbContext db)
    {
        RuleFor(c => c.TaskId)
            .NotEmpty();

        RuleFor(c => c.CommentId)
            .NotEmpty();

        RuleFor(c => c.NewContent)
            .NotEmpty()
            .MaximumLength(10_000);

        RuleFor(c => c.RequestedByUserId)
            .NotEmpty();

        // Verify that the requesting user is the original author of the comment.
        // This is an ownership check, not an authorization policy - it enforces
        // the domain rule that only the author may edit their own comment.
        RuleFor(c => c)
            .MustAsync(async (cmd, ct) =>
                await db.Comments.AnyAsync(
                    c => c.Id == cmd.CommentId
                        && c.TaskId == cmd.TaskId
                        && c.AuthorId == cmd.RequestedByUserId
                        && !c.IsDeleted,
                    ct))
            .WithMessage("Comment not found or user is not the author.");
    }
}
