using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class DeleteProjectHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_DeletesProject()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        Project project = ProjectBuilder.Create(
            organizationId: Guid.CreateVersion7(),
            createdByUserId: Guid.CreateVersion7(),
            name: "Test Project",
            slug: "test-project",
            description: "A test project");

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var handler = new DeleteProjectHandler(db, new FakeTimeProvider(Now));

        var command = new DeleteProject(ProjectId: project.Id);

        // Act
        await handler.HandleAsync(command);

        // Assert — reload succeeds because TestAppDbContext has no IsDeleted global filter
        Project reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.True(reloaded.IsDeleted);
        Assert.Equal(Now, reloaded.DeletedAt);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new DeleteProjectHandler(db, new FakeTimeProvider(Now));
        var command = new DeleteProject(ProjectId: Guid.CreateVersion7());

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(command));
    }
}
