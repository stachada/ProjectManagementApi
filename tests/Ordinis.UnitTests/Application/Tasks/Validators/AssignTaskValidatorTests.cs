using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;
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
        => new (
            TaskId: taskId,
            AssigneeId: assigneeId,
            RequestedByUserId: requestedByUserId);

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
        var validator = new AssignTaskValidator(db);

        TestValidationResult<AssignTask> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.AssigneeId)
            .WithErrorMessage("Assignee does not exist.");
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
}
