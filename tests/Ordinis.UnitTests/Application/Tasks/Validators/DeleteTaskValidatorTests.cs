using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="DeleteTaskValidator"/> rules.
/// </summary>
public sealed class DeleteTaskValidatorTests
{
    private static DeleteTask ValidCommand()
        => new(Guid.CreateVersion7(), Guid.CreateVersion7(), [1, 2, 3, 4]);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new DeleteTaskValidator();

        TestValidationResult<DeleteTask> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_NullIfMatch_HasValidationErrorForIfMatch()
    {
        var validator = new DeleteTaskValidator();
        DeleteTask command = ValidCommand() with { IfMatch = null };

        TestValidationResult<DeleteTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }
}
