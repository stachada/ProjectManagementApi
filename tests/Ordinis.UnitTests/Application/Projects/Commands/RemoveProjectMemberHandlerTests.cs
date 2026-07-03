using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class RemoveProjectMemberHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_RemovesMember()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var memberUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create();
        project.AddMember(memberUserId, Role.Member, Now);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await new RemoveProjectMemberHandler(db)
            .HandleAsync(new RemoveProjectMember(project.Id, memberUserId));

        bool stillMember = await db.ProjectMembers
            .AnyAsync(m => m.ProjectId == project.Id && m.UserId == memberUserId);
        Assert.False(stillMember);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RemoveProjectMemberHandler(db)
                .HandleAsync(new RemoveProjectMember(Guid.CreateVersion7(), Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_NotAMember_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new RemoveProjectMemberHandler(db)
                .HandleAsync(new RemoveProjectMember(project.Id, Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_LastAdmin_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var createdByUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: createdByUserId);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // Creator is the only Admin — removing them must be blocked
        await Assert.ThrowsAsync<DomainException>(() =>
            new RemoveProjectMemberHandler(db)
                .HandleAsync(new RemoveProjectMember(project.Id, createdByUserId)));
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
            new RemoveProjectMemberHandler(db)
                .HandleAsync(new RemoveProjectMember(project.Id, memberUserId)));
    }
}
