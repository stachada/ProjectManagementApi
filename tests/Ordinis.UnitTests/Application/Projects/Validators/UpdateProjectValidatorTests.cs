using FluentValidation.TestHelper;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="UpdateProjectValidator"/> rules.
/// </summary>
public sealed class UpdateProjectValidatorTests
{
    private static readonly ISlugGenerator SlugGenerator = new SlugGenerator();

    private static UpdateProject ValidCommand(Guid? projectId = null, string newName = "Updated Name", string? newDescription = null, byte[]? ifMatch = null)
        => new(projectId ?? ProjectBuilder.Create().Id, newName, newDescription, ifMatch ?? [1, 2, 3, 4]);

    [Fact]
    public async Task TestValidate_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidate_EmptyProjectId_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand(projectId: Guid.Empty);

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidate_NewNameEmptyOrWhitespace_HasValidationErrorForNewName(string newName)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand(newName: newName);

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidate_NewNameExceedsMaxLength_HasValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand(newName: new string('a', 101));

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidate_NewNameAtMaxLength_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand(newName: new string('a', 100));

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidate_NewDescriptionExceedsMaxLength_HasValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand(newDescription: new string('a', 1001));

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public async Task TestValidate_NewDescriptionAtMaxLength_HasNoValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand(newDescription: new string('a', 1000));

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public async Task TestValidate_NewDescriptionNull_HasNoValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand(newDescription: null);

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public async Task TestValidate_NullIfMatch_HasValidationErrorForIfMatch()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateProjectValidator(db, SlugGenerator);
        UpdateProject command = ValidCommand() with { IfMatch = null };

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }


    // GAP-10: a rename must not clash with another project of the organization — by slug or by
    // current name, ignoring case — but renaming a project to (a variant of) its own name is fine.
    [Theory]
    [InlineData("Hermes", true)]          // another project's name
    [InlineData("HERMES", true)]          // ignoring case
    [InlineData("Zeus Messaging", true)]  // another project's current (renamed) name
    [InlineData("APOLLO", false)]         // its own name in other casing
    [InlineData("Apollo 2", false)]       // free
    public async Task TestValidateAsync_RenameClash_DecidesValidity(string newName, bool expectError)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Guid organizationId = Guid.CreateVersion7();
        Project apollo = ProjectBuilder.Create(organizationId: organizationId, name: "Apollo", slug: "apollo");
        Project hermes = ProjectBuilder.Create(organizationId: organizationId, name: "Hermes", slug: "hermes");
        Project zeus = ProjectBuilder.Create(organizationId: organizationId, name: "Zeus", slug: "zeus");
        zeus.Rename("Zeus Messaging");
        Project otherTenant = ProjectBuilder.Create(organizationId: Guid.CreateVersion7(), name: "Apollo 2", slug: "apollo-2");
        db.Projects.AddRange(apollo, hermes, zeus, otherTenant);
        await db.SaveChangesAsync();
        var validator = new UpdateProjectValidator(db, SlugGenerator);

        TestValidationResult<UpdateProject> result = await validator.TestValidateAsync(ValidCommand(projectId: apollo.Id, newName: newName));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.NewName);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.NewName);
        }
    }
}
