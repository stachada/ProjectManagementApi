namespace Ordinis.Application.Tasks.Dtos;

/// <summary>
/// Represents a single comment as returned in <see cref="TaskDto"/>.
/// </summary>
public sealed class CommentDto
{
    /// <summary>
    /// Unique identifier of the comment.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// User ID of the comment author.
    /// </summary>
    public Guid AuthorId { get; init; }

    /// <summary>
    /// Display name of the comment author, resolved from the User aggregate.
    /// </summary>
    public string AuthorDisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Full text content of the comment.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Whether this comment has been edited at least once since it was posted.
    /// Corresponds to <see cref="Comment.IsEdited"/> on the domain entity.
    /// </summary>
    public bool IsEdited { get; init;}

    /// <summary>
    /// UTC timestamp when this comment was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent edit, or <see cref="null"/> if the comment
    /// has never been edited.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
