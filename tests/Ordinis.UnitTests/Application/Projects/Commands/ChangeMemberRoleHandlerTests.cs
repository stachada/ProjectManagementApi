using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class ChangeMemberRoleHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesMemberRole()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var memberUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create();
        project.AddMember(memberUserId, Role.Member, Now);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await new ChangeMemberRoleHandler(db)
            .HandleAsync(new ChangeMemberRole(project.Id, memberUserId, Role.Viewer, project.RowVersion));

        ProjectMember member = await db.ProjectMembers
            .SingleAsync(m => m.ProjectId == project.Id && m.UserId == memberUserId);
        Assert.Equal(Role.Viewer, member.Role);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ChangeMemberRoleHandler(db)
                .HandleAsync(new ChangeMemberRole(Guid.CreateVersion7(), Guid.CreateVersion7(), Role.Member, null)));
    }

    [Fact]
    public async Task HandleAsync_NotAMember_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new ChangeMemberRoleHandler(db)
                .HandleAsync(new ChangeMemberRole(project.Id, Guid.CreateVersion7(), Role.Member, project.RowVersion)));
    }

    [Fact]
    public async Task HandleAsync_DemotingLastAdmin_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var createdByUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: createdByUserId);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // Creator is the only Admin — demoting them must be blocked
        await Assert.ThrowsAsync<DomainException>(() =>
            new ChangeMemberRoleHandler(db)
                .HandleAsync(new ChangeMemberRole(project.Id, createdByUserId, Role.Member, project.RowVersion)));
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var memberUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create();
        project.AddMember(memberUserId, Role.Member, Now);
        project.Archive();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new ChangeMemberRoleHandler(db)
                .HandleAsync(new ChangeMemberRole(project.Id, memberUserId, Role.Viewer, project.RowVersion)));
    }
}
