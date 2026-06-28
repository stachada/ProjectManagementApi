using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Tasks;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="UpdateTaskValidator"/> rules.
/// </summary>
public sealed class UpdateTaskValidatorTests
{
    private static UpdateTask ValidCommand(Guid taskId)
        => new (
            TaskId: taskId,
            Title: "Fix login bug",
            Description: "Description",
            Priority: Priority.Medium,
            DueDate: null,
            RequestedByUserId: Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new UpdateTaskValidator();

        TestValidationResult<UpdateTask> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTaskId_HasValidationErrorForTaskId()
    {
        var validator = new UpdateTaskValidator();

        TestValidationResult<UpdateTask> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTitle_HasValidationErrorForTitle()
    {
        var validator = new UpdateTaskValidator();
        UpdateTask command = ValidCommand(Guid.CreateVersion7()) with { Title = string.Empty };

        TestValidationResult<UpdateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public async Task TestValidateAsync_TitleTooLong_HasValidationErrorForTitle()
    {
        var validator = new UpdateTaskValidator();
        UpdateTask command = ValidCommand(Guid.CreateVersion7()) with { Title = new string('A', 201) };

        TestValidationResult<UpdateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public async Task TestValidateAsync_TitleAtMaxLength_HasNoValidationErrorForTitle()
    {
        var validator = new UpdateTaskValidator();
        UpdateTask command = ValidCommand(Guid.CreateVersion7()) with { Title = new string('A', 200) };

        TestValidationResult<UpdateTask> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public async Task TestValidateAsync_PriorityOutOfRange_HasValidationErrorForPriority()
    {
        var validator = new UpdateTaskValidator();
        UpdateTask command = ValidCommand(Guid.CreateVersion7()) with { Priority = (Priority)99 };

        TestValidationResult<UpdateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Priority)
            .WithErrorMessage("Priority must be a valid Priority value.");
    }

    [Fact]
    public async Task TestValidateAsync_EmptyRequestedByUserId_HasValidationErrorForRequestedByUserId()
    {
        var validator = new UpdateTaskValidator();
        UpdateTask command = ValidCommand(Guid.CreateVersion7()) with { RequestedByUserId = Guid.Empty };

        TestValidationResult<UpdateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.RequestedByUserId);
    }
}
