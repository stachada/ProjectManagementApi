using Ordinis.Application.Tasks.Dtos;

namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Full board detail view returned by <c>GET /api/v1/boards/{id}</c>.
/// Includes a capped list of the most recently created tasks (up to
/// <see cref="MaxEmbeddedTasks"/>). For paginated task access use
/// <c>GET /api/v1/boards/{id}/tasks</c>.
/// </summary>
public sealed record BoardDto
{
    /// <summary>
    /// Maximum number of tasks embedded in this DTO.
    /// Clients needing more should call <c>GET /api/v1/boards/{id}/tasks</c>.
    /// </summary>
    public const int MaxEmbeddedTasks = 50;

    /// <summary>Unique board identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Display name of the board.</summary>
    public required string Name { get; init; }

    /// <summary>Optional description of the board's purpose.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the board is archived (read-only).</summary>
    public required bool IsArchived { get; init; }

    /// <summary>The ID of the project this board belongs to.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>The ID of the user who created the board.</summary>
    public required Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Total number of tasks on this board, including tasks beyond
    /// the <see cref="MaxEmbeddedTasks"/> cap.
    /// </summary>
    public required int TaskCount { get; init; }

    /// <summary>
    /// The most recently created tasks on this board, capped at
    /// <see cref="MaxEmbeddedTasks"/>. Ordered by <c>CreatedAt</c> descending.
    /// </summary>
    public required IReadOnlyList<TaskSummaryDto> Tasks { get; init; }

    /// <summary>UTC timestamp when the board was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp when the board was last modified.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// True when <see cref="Tasks"/> does not contain all tasks on this board.
    /// Derived from <c>Tasks.Count &lt; TaskCount</c> — no mapper input required.
    /// Call <c>GET /api/v1/boards/{id}/tasks</c> to retrieve the full paginated list.
    /// </summary>
    public bool TasksAreTruncated => Tasks.Count < TaskCount;
}
