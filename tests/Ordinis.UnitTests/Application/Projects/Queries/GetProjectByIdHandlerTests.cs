using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetProjectByIdHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsCorrectDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        User creator = UserBuilder.Create(displayName: "Alice");
        db.Users.Add(creator);

        Project project = ProjectBuilder.Create(
            createdByUserId: creator.Id,
            name: "My Project",
            slug: "my-project",
            description: "A project");
        db.Projects.Add(project);

        Board board = BoardBuilder.Create(projectId: project.Id, name: "Sprint 1");
        db.Boards.Add(board);

        db.Tasks.Add(TaskBuilder.Create(boardId: board.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: board.Id));
        await db.SaveChangesAsync();

        ProjectDto dto = await new GetProjectByIdHandler(db)
            .HandleAsync(new GetProjectById(project.Id));

        Assert.Equal(project.Id, dto.Id);
        Assert.Equal("My Project", dto.Name);
        Assert.Equal("my-project", dto.Slug);
        Assert.Equal("A project", dto.Description);
        Assert.Equal(project.OrganizationId, dto.OrganizationId);
        Assert.Equal(1, dto.BoardCount);
        Assert.Equal(1, dto.MemberCount);

        BoardSummaryDto boardDto = Assert.Single(dto.Boards);
        Assert.Equal(board.Id, boardDto.Id);
        Assert.Equal(2, boardDto.TaskCount);

        ProjectMemberDto memberDto = Assert.Single(dto.Members);
        Assert.Equal(creator.Id, memberDto.UserId);
        Assert.Equal("Alice", memberDto.DisplayName);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetProjectByIdHandler(db)
                .HandleAsync(new GetProjectById(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_MultipleBoardsWithTasks_TaskCountsResolvedPerBoard()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);

        Board board1 = BoardBuilder.Create(projectId: project.Id, name: "Board A");
        Board board2 = BoardBuilder.Create(projectId: project.Id, name: "Board B");
        db.Boards.Add(board1);
        db.Boards.Add(board2);

        db.Tasks.Add(TaskBuilder.Create(boardId: board1.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: board1.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: board2.Id));
        await db.SaveChangesAsync();

        ProjectDto dto = await new GetProjectByIdHandler(db)
            .HandleAsync(new GetProjectById(project.Id));

        Assert.Equal(2, dto.BoardCount);
        BoardSummaryDto dto1 = dto.Boards.Single(b => b.Id == board1.Id);
        BoardSummaryDto dto2 = dto.Boards.Single(b => b.Id == board2.Id);
        Assert.Equal(2, dto1.TaskCount);
        Assert.Equal(1, dto2.TaskCount);
    }

    [Fact]
    public async Task HandleAsync_MemberUserNotInUsersTable_DisplayNameFallsBackToUnknown()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        // No User row added — member's UserId will be missing from the lookup.
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        ProjectDto dto = await new GetProjectByIdHandler(db)
            .HandleAsync(new GetProjectById(project.Id));

        ProjectMemberDto member = Assert.Single(dto.Members);
        Assert.Equal("Unknown", member.DisplayName);
    }
}
