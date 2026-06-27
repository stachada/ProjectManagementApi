using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Pure static mapping functions from domain objects to DTOs.
/// All methods are side-effect free — no I/O, no DI dependencies.
/// Cross-aggregate data (user display names) is resolved by the calling
/// handler and passed in as a lookup dictionary, keeping these functions
/// testable without any infrastructure setup.
/// </summary>
public static class ProjectMapper
{
    /// <summary>
    /// Maps a <see cref="Project"/> to a <see cref="ProjectDto"/> full detail view.
    /// Boards are ordered by <c>CreatedAt</c> ascending, capped at
    /// <see cref="ProjectDto.MaxEmbeddedCollectionSize"/>.
    /// Members are ordered by <c>JoinedAt</c> ascending, capped at the same limit.
    /// </summary>
    /// <param name="project">The project to map. Must not be null.</param>
    /// <param name="userLookup">
    /// Display names keyed by user ID. Must contain an entry for every
    /// <see cref="ProjectMember.UserId"/> in <paramref name="project"/>.
    /// </param>
    /// <param name="boardTaskCounts">
    /// Task counts keyed by board ID. Must contain an entry for every
    /// board in <paramref name="boards"/>.
    /// </param>
    /// <param name="boards">
    /// All boards belonging to <paramref name="project"/>. <see cref="Board"/> is an
    /// independent aggregate root with no navigation from <see cref="Project"/>, so
    /// the caller must load and supply them explicitly.
    /// </param>
    public static ProjectDto ToDto(
        this Project project,
        IReadOnlyDictionary<Guid, string> userLookup,
        IReadOnlyDictionary<Guid, int> boardTaskCounts,
        IReadOnlyList<Board> boards)
    {
        // Materialise once so we can count the full set before capping.
        var allMembers = project.Members.ToList();

        var boardDtos = boards
            .OrderBy(b => b.CreatedAt)
            .Take(ProjectDto.MaxEmbeddedCollectionSize)
            .Select(b => b.ToSummaryDto(boardTaskCounts.GetValueOrDefault(b.Id)))
            .ToList();

        var members = allMembers
            .OrderBy(m => m.JoinedAt)
            .Take(ProjectDto.MaxEmbeddedCollectionSize)
            .Select(m => m.ToMemberDto(userLookup))
            .ToList();

        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Slug = project.Slug,
            Description = project.Description,
            IsArchived = project.IsArchived,
            OrganizationId = project.OrganizationId,
            CreatedByUserId = project.CreatedByUserId,
            BoardCount = boards.Count,
            MemberCount = allMembers.Count,
            Boards = boardDtos,
            Members = members,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    /// <summary>
    /// Maps a <see cref="ProjectMember"/> to a <see cref="ProjectMemberDto"/>.
    /// </summary>
    /// <param name="member">The membership record to map.</param>
    /// <param name="userLookup">Display names keyed by user ID.</param>
    public static ProjectMemberDto ToMemberDto(
        this ProjectMember member,
        IReadOnlyDictionary<Guid, string> userLookup)
        => new()
        {
            Id = member.Id,
            UserId = member.UserId,
            DisplayName = userLookup.TryGetValue(member.UserId, out var name) ? name : "Unknown",
            Role = member.Role,
            JoinedAt = member.JoinedAt
        };

    /// <summary>
    /// Maps a <see cref="Board"/> to a <see cref="BoardSummaryDto"/>.
    /// <see cref="Board"/> carries no task navigation collection, so the
    /// task count is always supplied explicitly by the calling handler.
    /// </summary>
    /// <param name="board">The board to map.</param>
    /// <param name="taskCount">Pre-resolved task count for this board.</param>
    public static BoardSummaryDto ToSummaryDto(this Board board, int taskCount)
        => new()
        {
            Id = board.Id,
            Name = board.Name,
            Description = board.Description,
            IsArchived = board.IsArchived,
            ProjectId = board.ProjectId,
            CreatedByUserId = board.CreatedByUserId,
            TaskCount = taskCount,
            CreatedAt = board.CreatedAt
        };

    /// <summary>
    /// Maps a <see cref="Board"/> to a <see cref="BoardDto"/> full detail view.
    /// </summary>
    /// <param name="board">The board to map.</param>
    /// <param name="taskCount">Total task count on this board (may exceed embedded list size).</param>
    /// <param name="tasks">
    /// Capped task list (up to <see cref="BoardDto.MaxEmbeddedTasks"/>),
    /// already mapped to <see cref="TaskSummaryDto"/> by the handler.
    /// </param>
    public static BoardDto ToBoardDto(
        this Board board,
        int taskCount,
        IReadOnlyList<TaskSummaryDto> tasks)
        => new()
        {
            Id = board.Id,
            Name = board.Name,
            Description = board.Description,
            IsArchived = board.IsArchived,
            ProjectId = board.ProjectId,
            CreatedByUserId = board.CreatedByUserId,
            TaskCount = taskCount,
            Tasks = tasks,
            CreatedAt = board.CreatedAt,
            UpdatedAt = board.UpdatedAt
        };
}
