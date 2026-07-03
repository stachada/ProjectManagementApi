using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class CreateProjectHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly ISlugGenerator SlugGenerator = new SlugGenerator();

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesProject()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new CreateProjectHandler(db, new FakeTimeProvider(Now), SlugGenerator);

        var command = new CreateProject(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Test Project",
            Description: "A test project");

        Guid projectId = await handler.HandleAsync(command);

        Project reloaded = await db.Projects.SingleAsync(p => p.Id == projectId);

        Assert.Equal(command.OrganizationId, reloaded.OrganizationId);
        Assert.Equal(command.CreatedByUserId, reloaded.CreatedByUserId);
        Assert.Equal(command.Name, reloaded.Name);
        Assert.Equal(command.Description, reloaded.Description);
        Assert.Equal("test-project", reloaded.Slug);
        Assert.False(reloaded.IsArchived);
    }

    [Fact]
    // This is validated by handler's validator
    public async Task HandleAsync_SameNameDifferentOrganization_CreatesProjectsWithSameSlug()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new CreateProjectHandler(db, new FakeTimeProvider(Now), SlugGenerator);

        var command1 = new CreateProject(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Test Project");

        var command2 = new CreateProject(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Test Project");

        Guid projectId1 = await handler.HandleAsync(command1);
        Guid projectId2 = await handler.HandleAsync(command2);

        Project reloaded1 = await db.Projects.SingleAsync(p => p.Id == projectId1);
        Project reloaded2 = await db.Projects.SingleAsync(p => p.Id == projectId2);

        Assert.Equal("test-project", reloaded1.Slug);
        Assert.Equal("test-project", reloaded2.Slug);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatorAddedAsAdminMember()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateProjectHandler(db, new FakeTimeProvider(Now), SlugGenerator);
        var command = new CreateProject(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Test Project");

        // Act
        Guid projectId = await handler.HandleAsync(command);

        // Assert
        ProjectMember member = await db.ProjectMembers.SingleAsync(m => m.ProjectId == projectId);
        Assert.Equal(command.CreatedByUserId, member.UserId);
        Assert.Equal(Role.Admin, member.Role);
        Assert.Equal(Now, member.JoinedAt);
    }

    [Fact]
    public async Task HandleAsync_EmptyDescription_CreatesProjectWithNullDescription()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new CreateProjectHandler(db, new FakeTimeProvider(Now), SlugGenerator);

        var command = new CreateProject(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Test Project",
            Description: "");

        Guid projectId = await handler.HandleAsync(command);

        Project reloaded = await db.Projects.SingleAsync(p => p.Id == projectId);

        Assert.Null(reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsArgumentException()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new CreateProjectHandler(db, new FakeTimeProvider(Now), SlugGenerator);

        var command = new CreateProject(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "",
            Description: "A test project");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_OrganizationIdEmpty_ThrowsArgumentException()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new CreateProjectHandler(db, new FakeTimeProvider(Now), SlugGenerator);

        var command = new CreateProject(
            OrganizationId: Guid.Empty, // Empty organization ID
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Test Project",
            Description: "A test project");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command));
    }

    [Fact]
    public async Task HandleAsync_CreatedByUserIdEmpty_ThrowsArgumentException()
    {
        // Arrange
        using TestAppDbContext db = TestDbContextFactory.Create();

        var handler = new CreateProjectHandler(db, new FakeTimeProvider(Now), SlugGenerator);

        var command = new CreateProject(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.Empty, // Empty created by user ID
            Name: "Test Project",
            Description: "A test project");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command));
    }
}
