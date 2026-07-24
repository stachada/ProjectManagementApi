using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

/// <summary>
/// Verifies <see cref="UpdateProjectHandler"/> updates <see cref="Project"/> with the
/// submitted fields, and returns the updated project's ID.
/// </summary>
public class UpdateProjectHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesProject()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        Project project = ProjectBuilder.Create(
            organizationId: Guid.CreateVersion7(),
            createdByUserId: Guid.CreateVersion7(),
            name: "Old Name",
            slug: "old-name",
            description: "Old description");

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var handler = new UpdateProjectHandler(db);

        var command = new UpdateProject(
            ProjectId: project.Id,
            NewName: "New Name",
            NewDescription: "New description",
            IfMatch: project.RowVersion);

        // Act
        await handler.HandleAsync(command);

        // Assert
        Project reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);

        Assert.Equal(command.NewName, reloaded.Name);
        Assert.Equal(command.NewDescription, reloaded.Description);
        Assert.Equal("old-name", reloaded.Slug); // Slug is immutable after creation
    }

    [Fact]
    public async Task HandleAsync_NullNewDescription_ClearsDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create(description: "Old description");
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await new UpdateProjectHandler(db).HandleAsync(
            new UpdateProject(project.Id, "New Name", NewDescription: null, IfMatch: project.RowVersion));

        Project reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Null(reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_EmptyNewDescription_ClearsDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create(description: "Old description");
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await new UpdateProjectHandler(db).HandleAsync(
            new UpdateProject(project.Id, "New Name", NewDescription: "", IfMatch: project.RowVersion));

        Project reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Null(reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_NotExistingProject_ThrowsNotFoundException()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new UpdateProjectHandler(db);

        var command = new UpdateProject(
            ProjectId: Guid.CreateVersion7(), // Non-existent project ID
            NewName: "New Name",
            NewDescription: "New description",
            IfMatch: null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsArgumentException()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        Project project = ProjectBuilder.Create(
            organizationId: Guid.CreateVersion7(),
            createdByUserId: Guid.CreateVersion7(),
            name: "Old Name",
            slug: "old-name",
            description: "Old description");

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var handler = new UpdateProjectHandler(db);

        var command = new UpdateProject(
            ProjectId: project.Id,
            NewName: "", // Empty name
            NewDescription: "New description",
            IfMatch: project.RowVersion);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_Archived_ThrowsDomainException()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        Project project = ProjectBuilder.Create(
            organizationId: Guid.CreateVersion7(),
            createdByUserId: Guid.CreateVersion7(),
            name: "Old Name",
            slug: "old-name",
            description: "Old description");

        project.Archive();

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var handler = new UpdateProjectHandler(db);

        var command = new UpdateProject(
            ProjectId: project.Id,
            NewName: "New Name",
            NewDescription: "New description",
            IfMatch: project.RowVersion);

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_RowVersionChangedSinceLoad_ThrowsConcurrencyException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create(
            organizationId: Guid.CreateVersion7(),
            createdByUserId: Guid.CreateVersion7(),
            name: "Old Name",
            slug: "old-name",
            description: "Old description");
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // Simulate another request having already saved a conflicting change between this
        // context's load and its save, by making the tracked RowVersion's original value
        // stale relative to what's in the store - the same mismatch EF Core would detect
        // if a second context had updated the row in between.
        db.Entry(project).Property(t => t.RowVersion).OriginalValue = [1, 2, 3, 4, 5, 6, 7, 8];

        var handler = new UpdateProjectHandler(db);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            handler.HandleAsync(
                new UpdateProject(project.Id, "Lost the race", "New description", IfMatch: null),
                CancellationToken.None));
    }
}
