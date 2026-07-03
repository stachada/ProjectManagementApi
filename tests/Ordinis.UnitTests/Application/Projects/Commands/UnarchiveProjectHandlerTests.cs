using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class UnarchiveProjectHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_UnarchivesProject()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        project.Archive();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await new UnarchiveProjectHandler(db).HandleAsync(new UnarchiveProject(project.Id));

        Project reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.False(reloaded.IsArchived);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UnarchiveProjectHandler(db).HandleAsync(new UnarchiveProject(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_NotArchived_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new UnarchiveProjectHandler(db).HandleAsync(new UnarchiveProject(project.Id)));
    }
}
