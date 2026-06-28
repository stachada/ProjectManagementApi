using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="AddCommentValidator"/> rules.
/// </summary>
public sealed class AddCommentValidatorTests
{
    private static AddComment ValidCommand(Guid taskId, Guid authorId)
        => new (
            TaskId: taskId,
            AuthorId: authorId,
            Content: "This is a comment.");

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new AddCommentValidator();

        TestValidationResult<AddComment> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTaskId_HasValidationErrorForTaskId()
    {
        var validator = new AddCommentValidator();

        TestValidationResult<AddComment> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.Empty,
                Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyContent_HasValidationErrorForContent()
    {
        var validator = new AddCommentValidator();
        AddComment command = ValidCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()) with { Content = string.Empty };

        TestValidationResult<AddComment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Content);
    }

    [Fact]
    public async Task TestValidateAsync_MaxContentLength_HasNoValidationErrors()
    {
        var validator = new AddCommentValidator();
        AddComment command = ValidCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()) with { Content = new string('A', 10_000) };

        TestValidationResult<AddComment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_ContentTooLong_HasValidationErrorForContent()
    {
        var validator = new AddCommentValidator();
        AddComment command = ValidCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()) with { Content = new string('A', 10_001) };

        TestValidationResult<AddComment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Content);
    }
}
