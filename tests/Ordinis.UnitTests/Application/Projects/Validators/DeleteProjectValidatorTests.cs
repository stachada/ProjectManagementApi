using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="DeleteProjectValidator"/> rules.
/// </summary>
public sealed class DeleteProjectValidatorTests
{
    private static DeleteProject ValidCommand()
        => new(Guid.CreateVersion7(), [1, 2, 3, 4]);

    [Fact]
    public void TestValidate_ValidCommand_HasNoValidationErrors()
    {
        var validator = new DeleteProjectValidator();

        TestValidationResult<DeleteProject> result = validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TestValidate_NullIfMatch_HasValidationErrorForIfMatch()
    {
        var validator = new DeleteProjectValidator();
        DeleteProject command = ValidCommand() with { IfMatch = null };

        TestValidationResult<DeleteProject> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }
}
