namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Lean board view embedded in <see cref="ProjectDto"/> board collections
/// and returned by <c>GET /api/v1/projects/{id}/boards</c>.
/// Use <see cref="BoardDto"/> for the full board detail view including tasks.
/// </summary>
public sealed record BoardSummaryDto
{
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

    /// <summary>Total number of tasks on this board.</summary>
    public required int TaskCount { get; init; }

    /// <summary>UTC timestamp when the board was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
