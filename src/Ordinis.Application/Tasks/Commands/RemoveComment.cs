using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Soft-deletes a comment from a task.
/// </summary>
/// <param name="TaskId">The ID of the task that owns the comment.</param>
/// <param name="CommentId">The ID of the comment to remove.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
public sealed record RemoveComment(
    Guid TaskId,
    Guid CommentId,
    Guid RequestedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="RemoveComment"/> by invoking <see cref="ProjectTask.RemoveComment"/>.
/// </summary>
/// <remarks>
/// No validator is needed — the task ID is a non-empty GUID guaranteed by route binding
/// and authentication middleware. A missing task throws <see cref="NotFoundException"/>;
/// a missing or already-deleted comment throws <see cref="DomainException"/>
/// (<c>task.comment-not-found</c>) from the aggregate.
/// <para>
/// The <c>TaskContributor</c> policy only proves the caller is a Member of the task's project.
/// Whether they may remove <i>this</i> comment (author, project Admin, or org Admin) is enforced
/// by <see cref="ProjectTask.RemoveComment"/>; this handler resolves the moderator half of that
/// rule, which needs data from outside the task aggregate.
/// </para>
/// </remarks>
internal sealed class RemoveCommentHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<RemoveComment>
{
    public async Task HandleAsync(RemoveComment command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        // Archived board/project = read-only tasks (see TaskContainerGuard).
        await TaskContainerGuard.EnsureWritableAsync(db, task.BoardId, cancellationToken);

        bool requesterIsModerator = await IsModeratorAsync(task.BoardId, command.RequestedByUserId, cancellationToken);

        task.RemoveComment(command.CommentId, command.RequestedByUserId, requesterIsModerator, timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="userId"/> is an Admin of the board's project, or an org-level
    /// Admin of the project's organization.
    /// </summary>
    private async Task<bool> IsModeratorAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var project = await db.Boards
            .Where(b => b.Id == boardId)
            .Join(db.Projects, b => b.ProjectId, p => p.Id, (b, p) => new { p.Id, p.OrganizationId })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return false;
        }

        bool isOrgAdmin = await db.Users.AnyAsync(
            u => u.Id == userId && u.OrgRole == Role.Admin && u.OrganizationId == project.OrganizationId,
            cancellationToken);

        if (isOrgAdmin)
        {
            return true;
        }

        // Equality (not ordering) on the string-stored Role translates correctly in SQL.
        return await db.ProjectMembers.AnyAsync(
            m => m.ProjectId == project.Id && m.UserId == userId && m.Role == Role.Admin,
            cancellationToken);
    }
}
