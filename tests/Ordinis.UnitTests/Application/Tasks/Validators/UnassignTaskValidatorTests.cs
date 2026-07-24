using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="UnassignTaskValidator"/> rules.
/// </summary>
public sealed class UnassignTaskValidatorTests
{
    private static UnassignTask ValidCommand()
        => new(Guid.CreateVersion7(), Guid.CreateVersion7(), [1, 2, 3, 4]);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new UnassignTaskValidator();

        TestValidationResult<UnassignTask> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_NullIfMatch_HasValidationErrorForIfMatch()
    {
        var validator = new UnassignTaskValidator();
        UnassignTask command = ValidCommand() with { IfMatch = null };

        TestValidationResult<UnassignTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }
}
