using Ordinis.Domain.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Represents a comment posted on a <see cref="ProjectTask"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership:</b> Comments are owned by the <see cref="ProjectTask"/> aggregate.
/// Never create or delete a <c>Comment</c> directly from a handler — always go
/// through <see cref="ProjectTask.AddComment"/> and <see cref="ProjectTask.RemoveComment"/>,
/// which enforce invariants and raise the appropriate domain events.
/// </para>
/// <para>
/// <b>Editing:</b> Comments can be edited by their author via <see cref="UpdateContent"/>.
/// The <c>IsEdited</c> flag is set on first edit and never cleared, providing
/// a lightweight audit signal without requiring a full edit history table.
/// <c>UpdatedAt</c> (inherited from <see cref="AuditableEntity"/>) records
/// the timestamp of the most recent edit.
/// </para>
/// <para>
/// <b>Soft delete:</b> Removing a comment calls <see cref="AuditableEntity.SoftDelete"/>,
/// preserving the row in the database. The global EF Core query filter
/// (<c>HasQueryFilter(c => !c.IsDeleted)</c>) excludes soft-deleted comments
/// from all standard queries. This preserves referential integrity for the
/// audit log without exposing deleted content to API consumers.
/// </para>
/// <para>
/// <b>Authorization:</b> Only the comment author or a project Admin may edit
/// or delete a comment. This is enforced by policy in Phase 7 — the domain
/// only enforces that the comment belongs to the correct task.
/// </para>
/// </remarks>
public class Comment : AuditableEntity
{
    #region Properties
    /// <summary>
    /// The comment body. Supports markdown.
    /// Maximum length enforced by <c>AddCommentValidator</c> in the Application layer.
    /// </summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>
    /// Whether this comment has been edited at least once since it was posted.
    /// Set to <c>true</c> on first call to <see cref="UpdateContent"/>;
    /// never reset to <c>false</c>. Displayed as an "edited" indicator in the UI.
    /// </summary>
    public bool IsEdited { get; private set; }
    #endregion

    #region Foreign Keys
    /// <summary>
    /// The task this comment belongs to.
    /// </summary>
    public Guid TaskId { get; private set; }

    /// <summary>
    /// The user who posted this comment.
    /// </summary>
    public Guid AuthorId { get; private set; }
    #endregion

    #region Navigation Properties
    /// <summary>
    /// The task this comment belongs to.
    /// </summary>
    public ProjectTask? Task { get; private set; }

    /// <summary>
    /// The user who posted this comment.
    /// </summary>
    public User? Author { get; private set; }
    #endregion

    #region Constructor
    private Comment() { }

    /// <summary>
    /// Creates a new comment on a task.
    /// Called exclusively from <see cref="ProjectTask.AddComment"/> —
    /// never instantiate directly from a handler.
    /// </summary>
    /// <param name="taskId">The task being commented on. Must not be empty.</param>
    /// <param name="authorId">The user posting the comment. Must not be empty.</param>
    /// <param name="content">The comment body. Must not be empty.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="content"/> is empty, or if <paramref name="taskId"/>
    /// or <paramref name="authorId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    internal static Comment Create(Guid taskId, Guid authorId, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("TaskId cannot be empty.", nameof(taskId));
        }

        if (authorId == Guid.Empty)
        {
            throw new ArgumentException("AuthorId cannot be empty.", nameof(authorId));
        }

        return new Comment
        {
            TaskId = taskId,
            AuthorId = authorId,
            Content = content.Trim(),
            IsEdited = false
        };
    }
    #endregion

    #region Behaviour
    /// <summary>
    /// Updates the comment's content.
    /// Sets <see cref="IsEdited"/> to <c>true</c> on first call.
    /// </summary>
    /// <param name="newContent">The updated comment body. Must not be empty.</param>
    /// <exception cref="DomainException">
    /// Thrown if the comment has been soft-deleted — deleted comments
    /// cannot be edited.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="newContent"/> is empty or whitespace.
    /// </exception>
    public void UpdateContent(string newContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newContent);

        if (IsDeleted)
        {
            throw new DomainException(
                "Cannot edit a deleted comment.",
                "comment.update-deleted"
            );
        }

        Content = newContent.Trim();
        IsEdited = true;
    }
    #endregion
}
