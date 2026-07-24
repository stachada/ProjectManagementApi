using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class ArchiveProjectHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_ArchivesProject()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await new ArchiveProjectHandler(db).HandleAsync(new ArchiveProject(project.Id, project.RowVersion));

        Project reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.True(reloaded.IsArchived);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ArchiveProjectHandler(db).HandleAsync(new ArchiveProject(Guid.CreateVersion7(), null)));
    }

    [Fact]
    public async Task HandleAsync_AlreadyArchived_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        project.Archive();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new ArchiveProjectHandler(db).HandleAsync(new ArchiveProject(project.Id, project.RowVersion)));
    }
}
