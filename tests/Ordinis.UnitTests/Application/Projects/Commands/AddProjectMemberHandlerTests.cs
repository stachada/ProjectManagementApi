using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class AddProjectMemberHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_AddsMemberWithCorrectRoleAndJoinedAt()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var userId = Guid.CreateVersion7();
        await new AddProjectMemberHandler(db, new FakeTimeProvider(Now))
            .HandleAsync(new AddProjectMember(project.Id, userId, Role.Member, project.RowVersion));

        ProjectMember member = await db.ProjectMembers.SingleAsync(m => m.ProjectId == project.Id && m.UserId == userId);
        Assert.Equal(Role.Member, member.Role);
        Assert.Equal(Now, member.JoinedAt);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new AddProjectMemberHandler(db, new FakeTimeProvider(Now))
                .HandleAsync(new AddProjectMember(Guid.CreateVersion7(), Guid.CreateVersion7(), Role.Member, null)));
    }

    [Fact]
    public async Task HandleAsync_AlreadyMember_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var createdByUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: createdByUserId);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // The creator is auto-added as Admin on project creation
        await Assert.ThrowsAsync<DomainException>(() =>
            new AddProjectMemberHandler(db, new FakeTimeProvider(Now))
                .HandleAsync(new AddProjectMember(project.Id, createdByUserId, Role.Viewer, project.RowVersion)));
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        project.Archive();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new AddProjectMemberHandler(db, new FakeTimeProvider(Now))
                .HandleAsync(new AddProjectMember(project.Id, Guid.CreateVersion7(), Role.Member, project.RowVersion)));
    }
}
