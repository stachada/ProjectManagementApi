using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Dtos;

/// <summary>
/// Verifies <see cref="ProjectMapper"/> field mapping. Pure function tests -
/// no EF Core, no DI, no async; boards and members are attached via the
/// domain's own public API (<see cref="Project.AddMember"/>) rather than
/// constructed directly.
/// </summary>
public class ProjectMapperTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    #region ToSummaryDto
    [Fact]
    public void ToSummaryDto_AllFieldsMapCorrectly()
    {
        Project project = ProjectBuilder.Create(
            organizationId: Guid.CreateVersion7(),
            createdByUserId: Guid.CreateVersion7(),
            name: "Project Alpha",
            slug: "project-alpha",
            description: "First project",
            now: Now);
        project.CreatedAt = Now;

        DateTimeOffset member1Added = Now.AddHours(1);
        var member1Id = Guid.CreateVersion7();
        project.AddMember(member1Id, Role.Admin, member1Added);
        DateTimeOffset member2Added = Now.AddHours(2);
        var member2Id = Guid.CreateVersion7();
        project.AddMember(member2Id, Role.Member, member2Added);

        ProjectSummaryDto dto = project.ToSummaryDto(boardCount: 5);

        Assert.Equal(project.Id, dto.Id);
        Assert.Equal("Project Alpha", dto.Name);
        Assert.Equal("project-alpha", dto.Slug);
        Assert.Equal("First project", dto.Description);
        Assert.Equal(project.IsArchived, dto.IsArchived);
        Assert.Equal(project.OrganizationId, dto.OrganizationId);
        Assert.Equal(project.CreatedByUserId, dto.CreatedByUserId);
        Assert.Equal(3, dto.MemberCount); // 2 added + 1 creator
        Assert.Equal(5, dto.BoardCount);
        Assert.Equal(Now, dto.CreatedAt);
    }

    [Fact]
    public void ToSummaryDto_BoardCountParameterFlowsThrough_IndependentOfMemberCount()
    {
        Project project = ProjectBuilder.Create(now: Now); // only the creator is a member

        ProjectSummaryDto noBoards = project.ToSummaryDto(boardCount: 0);
        ProjectSummaryDto manyBoards = project.ToSummaryDto(boardCount: 42);

        Assert.Equal(0, noBoards.BoardCount);
        Assert.Equal(42, manyBoards.BoardCount);
        Assert.Equal(1, noBoards.MemberCount);
        Assert.Equal(1, manyBoards.MemberCount);
    }
    #endregion

    #region ToDto - core fields
    [Fact]
    public void ToDto_AllFieldsMapCorrectly()
    {
        Project project = ProjectBuilder.Create(
            organizationId: Guid.CreateVersion7(),
            createdByUserId: Guid.CreateVersion7(),
            name: "Project Alpha",
            slug: "project-alpha",
            description: "First project",
            now: Now);
        project.CreatedAt = Now;
        project.UpdatedAt = Now.AddHours(1);

        ProjectDto dto = project.ToDto(
            userLookup: new Dictionary<Guid, string>(),
            boardTaskCounts: new Dictionary<Guid, int>(),
            boards: []);

        Assert.Equal(project.Id, dto.Id);
        Assert.Equal("Project Alpha", dto.Name);
        Assert.Equal("project-alpha", dto.Slug);
        Assert.Equal("First project", dto.Description);
        Assert.Equal(project.IsArchived, dto.IsArchived);
        Assert.Equal(project.OrganizationId, dto.OrganizationId);
        Assert.Equal(project.CreatedByUserId, dto.CreatedByUserId);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
        Assert.Equal(0, dto.BoardCount);
        Assert.Equal(1, dto.MemberCount); // just the creator
        Assert.Empty(dto.Boards);
        Assert.False(dto.BoardsAreTruncated);
        Assert.False(dto.MembersAreTruncated);
    }
    #endregion

    #region ToDto - boards
    [Fact]
    public void ToDto_EmbedsBoardSummaryDtosWithPerBoardTaskCount_OrderedByCreatedAt()
    {
        Project project = ProjectBuilder.Create(now: Now);
        Board second = BoardBuilder.Create(projectId: project.Id, name: "Second");
        second.CreatedAt = Now.AddMinutes(5);
        Board first = BoardBuilder.Create(projectId: project.Id, name: "First");
        first.CreatedAt = Now;
        var boardTaskCounts = new Dictionary<Guid, int> { [first.Id] = 3, [second.Id] = 7 };

        ProjectDto dto = project.ToDto(
            userLookup: new Dictionary<Guid, string>(),
            boardTaskCounts: boardTaskCounts,
            boards: [second, first]);

        Assert.Equal(["First", "Second"], dto.Boards.Select(b => b.Name));
        BoardSummaryDto firstDto = dto.Boards[0];
        Assert.Equal(first.Id, firstDto.Id);
        Assert.Equal(first.ProjectId, firstDto.ProjectId);
        Assert.Equal(first.CreatedByUserId, firstDto.CreatedByUserId);
        Assert.Equal(3, firstDto.TaskCount);
        Assert.Equal(7, dto.Boards[1].TaskCount);
    }

    [Fact]
    public void ToDto_BoardMissingFromTaskCountLookup_TaskCountDefaultsToZero()
    {
        Project project = ProjectBuilder.Create(now: Now);
        Board board = BoardBuilder.Create(projectId: project.Id);
        board.CreatedAt = Now;

        ProjectDto dto = project.ToDto(
            userLookup: new Dictionary<Guid, string>(),
            boardTaskCounts: new Dictionary<Guid, int>(),
            boards: [board]);

        Assert.Equal(0, Assert.Single(dto.Boards).TaskCount);
    }

    [Fact]
    public void ToDto_BoardsExceedingCap_AreTruncatedAndCountReflectsFullTotal()
    {
        Project project = ProjectBuilder.Create(now: Now);
        var boards = new List<Board>();
        for (var i = 0; i < ProjectDto.MaxEmbeddedCollectionSize + 1; i++)
        {
            Board board = BoardBuilder.Create(projectId: project.Id, name: $"Board {i}");
            board.CreatedAt = Now.AddMinutes(i);
            boards.Add(board);
        }

        ProjectDto dto = project.ToDto(
            userLookup: new Dictionary<Guid, string>(),
            boardTaskCounts: new Dictionary<Guid, int>(),
            boards: boards);

        Assert.Equal(ProjectDto.MaxEmbeddedCollectionSize + 1, dto.BoardCount);
        Assert.Equal(ProjectDto.MaxEmbeddedCollectionSize, dto.Boards.Count);
        Assert.True(dto.BoardsAreTruncated);
    }
    #endregion

    #region ToDto - members
    [Fact]
    public void ToDto_EmbedsProjectMemberDtos_OrderedByJoinedAtAndResolvesDisplayNames()
    {
        var creatorId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: creatorId, now: Now);
        var laterMemberId = Guid.CreateVersion7();
        project.AddMember(laterMemberId, Role.Viewer, Now.AddHours(2));
        var earlierMemberId = Guid.CreateVersion7();
        project.AddMember(earlierMemberId, Role.Member, Now.AddHours(1));
        var userLookup = new Dictionary<Guid, string>
        {
            [creatorId] = "Creator",
            [earlierMemberId] = "Earlier Member",
            [laterMemberId] = "Later Member"
        };

        ProjectDto dto = project.ToDto(
            userLookup: userLookup,
            boardTaskCounts: new Dictionary<Guid, int>(),
            boards: []);

        Assert.Equal(["Creator", "Earlier Member", "Later Member"], dto.Members.Select(m => m.DisplayName));
        ProjectMemberDto earlierDto = dto.Members[1];
        Assert.Equal(earlierMemberId, earlierDto.UserId);
        Assert.Equal(Role.Member, earlierDto.Role);
        Assert.Equal(Now.AddHours(1), earlierDto.JoinedAt);
    }

    [Fact]
    public void ToDto_MemberMissingFromUserLookup_DisplayNameFallsBackToUnknown()
    {
        Project project = ProjectBuilder.Create(now: Now); // creator not in lookup

        ProjectDto dto = project.ToDto(
            userLookup: new Dictionary<Guid, string>(),
            boardTaskCounts: new Dictionary<Guid, int>(),
            boards: []);

        Assert.Equal("Unknown", Assert.Single(dto.Members).DisplayName);
    }

    [Fact]
    public void ToDto_MembersExceedingCap_AreTruncatedAndCountReflectsFullTotal()
    {
        Project project = ProjectBuilder.Create(now: Now); // creator counts as member #1
        for (var i = 0; i < ProjectDto.MaxEmbeddedCollectionSize; i++)
        {
            project.AddMember(Guid.CreateVersion7(), Role.Viewer, Now.AddMinutes(i + 1));
        }

        ProjectDto dto = project.ToDto(
            userLookup: new Dictionary<Guid, string>(),
            boardTaskCounts: new Dictionary<Guid, int>(),
            boards: []);

        Assert.Equal(ProjectDto.MaxEmbeddedCollectionSize + 1, dto.MemberCount);
        Assert.Equal(ProjectDto.MaxEmbeddedCollectionSize, dto.Members.Count);
        Assert.True(dto.MembersAreTruncated);
    }
    #endregion
}
