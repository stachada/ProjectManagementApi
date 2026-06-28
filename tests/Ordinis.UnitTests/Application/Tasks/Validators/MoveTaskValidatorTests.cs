using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Tasks;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="MoveTaskValidator"/> rules.
/// </summary>
public sealed class MoveTaskValidatorTests
{
    private static MoveTask ValidCommand(Guid taskId)
        => new (
            TaskId: taskId,
            NewStatus: ProjectTaskStatus.InProgress,
            RequestedByUserId: Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new MoveTaskValidator();

        TestValidationResult<MoveTask> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTaskId_HasValidationErrorForTaskId()
    {
        var validator = new MoveTaskValidator();

        TestValidationResult<MoveTask> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public async Task TestValidateAsync_StatusOutOfRange_HasValidationErrorForStatus()
    {
        var validator = new MoveTaskValidator();
        MoveTask command = ValidCommand(Guid.CreateVersion7()) with { NewStatus = (ProjectTaskStatus)99 };

        TestValidationResult<MoveTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewStatus)
            .WithErrorMessage("NewStatus must be a valid ProjectTaskStatus value.");
    }
}
