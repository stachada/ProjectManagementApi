using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="AssignTaskValidator"/> rules, including the async user-existence
/// check run against the database.
/// </summary>
public sealed class AssignTaskValidatorTests
{
    private static AssignTask ValidCommand(Guid taskId, Guid assigneeId, Guid requestedByUserId)
        => new(
            TaskId: taskId,
            AssigneeId: assigneeId,
            RequestedByUserId: requestedByUserId,
            IfMatch: [1, 2, 3, 4]);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.CreateVersion7(),
                user.Id,
                Guid.CreateVersion7()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTaskId_HasValidationErrorForTaskId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.Empty,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyAssigneeId_HasValidationErrorForAssigneeId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.CreateVersion7(),
                Guid.Empty,
                Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.AssigneeId);
    }

    [Fact]
    public async Task TestValidateAsync_AssigneeDoesNotExist_HasValidationErrorForAssigneeId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        (Guid taskId, _, _) = await SeedTaskInProjectAsync(db);
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(taskId, Guid.CreateVersion7(), Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.AssigneeId)
            .WithErrorMessage("The assignee must be a Member or Admin of this task's project.");
    }

    // GAP-04: the assignee must be able to work on the task — authorization only checks the caller.
    [Theory]
    [InlineData(Role.Member, false)]
    [InlineData(Role.Admin, false)]
    [InlineData(Role.Viewer, true)]
    public async Task TestValidateAsync_AssigneeProjectRole_DecidesValidity(Role role, bool expectError)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        (Guid taskId, Project project, Guid organizationId) = await SeedTaskInProjectAsync(db);
        User assignee = UserBuilder.Create(organizationId: organizationId, email: "assignee@example.com");
        db.Users.Add(assignee);
        project.AddMember(assignee.Id, role, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(taskId, assignee.Id, Guid.CreateVersion7()));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.AssigneeId);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.AssigneeId);
        }
    }

    [Theory]
    [InlineData(false)] // same-organization user who is not a project member
    [InlineData(true)]  // user of another organization
    public async Task TestValidateAsync_AssigneeNotAProjectMember_HasValidationErrorForAssigneeId(bool otherOrganization)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        (Guid taskId, _, Guid organizationId) = await SeedTaskInProjectAsync(db);
        User outsider = UserBuilder.Create(
            organizationId: otherOrganization ? Guid.CreateVersion7() : organizationId,
            email: "outsider@example.com");
        db.Users.Add(outsider);
        await db.SaveChangesAsync();
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(taskId, outsider.Id, Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.AssigneeId);
    }

    [Fact]
    public async Task TestValidateAsync_AssigneeIsOrgAdmin_HasNoValidationErrorForAssigneeId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        (Guid taskId, _, Guid organizationId) = await SeedTaskInProjectAsync(db);
        User orgAdmin = UserBuilder.Create(organizationId: organizationId, email: "admin@example.com", orgRole: Role.Admin);
        db.Users.Add(orgAdmin);
        await db.SaveChangesAsync();
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(taskId, orgAdmin.Id, Guid.CreateVersion7()));

        result.ShouldNotHaveValidationErrorFor(x => x.AssigneeId);
    }

    /// <summary>A project (with its creator as Admin), a board and a task in it.</summary>
    private static async Task<(Guid TaskId, Project Project, Guid OrganizationId)> SeedTaskInProjectAsync(TestAppDbContext db)
    {
        User creator = UserBuilder.Create(email: "creator@example.com");
        Project project = ProjectBuilder.Create(organizationId: creator.OrganizationId, createdByUserId: creator.Id);
        Board board = BoardBuilder.Create(projectId: project.Id, createdByUserId: creator.Id);
        ProjectTask task = ProjectTask.Create(board.Id, creator.Id, "Fix login bug", DateTimeOffset.UtcNow);
        db.Users.Add(creator);
        db.Projects.Add(project);
        db.Boards.Add(board);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return (task.Id, project, creator.OrganizationId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyRequestedByUserId_HasValidationErrorForRequestedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.RequestedByUserId);
    }

    [Fact]
    public async Task TestValidateAsync_NullIfMatch_HasValidationErrorForIfMatch()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AssignTaskValidator(db);
        AssignTask command = ValidCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()) with { IfMatch = null };

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }
}
