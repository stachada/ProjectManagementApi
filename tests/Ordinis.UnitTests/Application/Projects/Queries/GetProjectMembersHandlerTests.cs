using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetProjectMembersHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsMembersWithDisplayNamesOrderedByJoinedAt()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        User creator = UserBuilder.Create(displayName: "Alice");
        User laterUser = UserBuilder.Create(displayName: "Bob");
        db.Users.AddRange(creator, laterUser);

        Project project = ProjectBuilder.Create(createdByUserId: creator.Id);
        project.AddMember(laterUser.Id, Role.Member, Now.AddHours(1));
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        IReadOnlyList<ProjectMemberDto> members = await new GetProjectMembersHandler(db)
            .HandleAsync(new GetProjectMembers(project.Id));

        Assert.Equal(2, members.Count);
        Assert.Equal("Alice", members[0].DisplayName);
        Assert.Equal("Bob", members[1].DisplayName);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetProjectMembersHandler(db)
                .HandleAsync(new GetProjectMembers(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_MemberUserNotInUsersTable_DisplayNameFallsBackToUnknown()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        IReadOnlyList<ProjectMemberDto> members = await new GetProjectMembersHandler(db)
            .HandleAsync(new GetProjectMembers(project.Id));

        Assert.Equal("Unknown", Assert.Single(members).DisplayName);
    }
}
