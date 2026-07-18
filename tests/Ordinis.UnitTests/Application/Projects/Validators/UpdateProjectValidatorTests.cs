using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="UpdateProjectValidator"/> rules.
/// </summary>
public sealed class UpdateProjectValidatorTests
{
    private static UpdateProject ValidCommand(Guid? projectId = null, string newName = "Updated Name", string? newDescription = null)
        => new(projectId ?? ProjectBuilder.Create().Id, newName, newDescription);

    [Fact]
    public void TestValidate_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);

        TestValidationResult<UpdateProject> result = validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TestValidate_EmptyProjectId_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);
        UpdateProject command = ValidCommand(projectId: Guid.Empty);

        TestValidationResult<UpdateProject> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TestValidate_NewNameEmptyOrWhitespace_HasValidationErrorForNewName(string newName)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);
        UpdateProject command = ValidCommand(newName: newName);

        TestValidationResult<UpdateProject> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public void TestValidate_NewNameExceedsMaxLength_HasValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);
        UpdateProject command = ValidCommand(newName: new string('a', 101));

        TestValidationResult<UpdateProject> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public void TestValidate_NewNameAtMaxLength_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);
        UpdateProject command = ValidCommand(newName: new string('a', 100));

        TestValidationResult<UpdateProject> result = validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public void TestValidate_NewDescriptionExceedsMaxLength_HasValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);
        UpdateProject command = ValidCommand(newDescription: new string('a', 1001));

        TestValidationResult<UpdateProject> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public void TestValidate_NewDescriptionAtMaxLength_HasNoValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);
        UpdateProject command = ValidCommand(newDescription: new string('a', 1000));

        TestValidationResult<UpdateProject> result = validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public void TestValidate_NewDescriptionNull_HasNoValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db);
        UpdateProject command = ValidCommand(newDescription: null);

        TestValidationResult<UpdateProject> result = validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }
}
