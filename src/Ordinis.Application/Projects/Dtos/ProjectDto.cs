using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Full project detail view returned by <c>GET /api/v1/projects/{id}</c>.
/// Embeds all boards (capped at 100) and all members (capped at 100).
/// Tasks are intentionally excluded - use <c>GET /api/v1/projects/{id}/tasks</c>
/// for paginated task access.
/// </summary>
public sealed record ProjectDto
{
    /// <summary>
    /// Safety cap on the number of embedded boards and members.
    /// Projects with more than this count require dedicated list endpoints.
    /// </summary>
    public const int MaxEmbeddedCollectionSize = 100;

    /// <summary>
    /// Unique project identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Display name of the project.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-friendly unique identifier scoped to the organization.
    /// Immutable after creation.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// Optional description of the project's purpose and scope.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the project is archived (read-only).
    /// </summary>
    public required bool IsArchived { get; init; }

    /// <summary>
    /// The ID of the organization this project belongs to.
    /// </summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// The ID of the user who created the project.
    /// </summary>
    public required Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Total number of boards in this project, including any beyond
    /// the <see cref="MaxEmbeddedCollectionSize"/> cap.
    /// </summary>
    public required int BoardCount { get; init; }

    /// <summary>
    /// Total number of members in this project, including any beyond
    /// the <see cref="MaxEmbeddedCollectionSize"/> cap.
    /// </summary>
    public required int MemberCount { get; init; }

    /// <summary>
    /// All boards in this project, capped at <see cref="MaxEmbeddedCollectionSize"/>.
    /// Ordered by <c>CreatedAt</c> ascending.>
    /// </summary>
    public required IReadOnlyList<BoardSummaryDto> Boards { get; init; }

    /// <summary>
    /// All members of this project, capped at <see cref="MaxEmbeddedCollectionSize"/>.
    /// Ordered by <c>JoinedAt</c> ascending.
    /// </summary>
    public required IReadOnlyList<ProjectMemberDto> Members { get; init; }

    /// <summary>
    /// UTC timestamp when the project was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp when the project was last modified.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// True when <see cref="Boards"/> does not contain all boards in this project.
    /// Derived from <c>Boards.Count < BoardCount</c> - no mapper input required.
    /// Call <c> GET /api/v1/projects/{id}/boards</c> for the full list.
    /// </summary>
    public bool BoardsAreTruncated => Boards.Count < BoardCount;

    /// <summary>
    /// True when <see cref="Members"/> does not contain all members of this project.
    /// Derived from <c>Members.Count &lt; MemberCount</c> — no mapper input required.
    /// Call <c>GET /api/v1/projects/{id}/members</c> for the full list.
    /// </summary>
    public bool MembersAreTruncated => Members.Count < MemberCount;
}
