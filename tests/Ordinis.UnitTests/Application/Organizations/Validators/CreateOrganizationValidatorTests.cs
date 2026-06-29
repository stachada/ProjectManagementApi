using FluentValidation.TestHelper;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Validators;

/// <summary>
/// Verifies <see cref="CreateOrganizationValidator"/> rules, including the
/// async globally-unique-slug check run against the database.
/// </summary>
public sealed class CreateOrganizationValidatorTests
{
    private static readonly ISlugGenerator SlugGenerator = new SlugGenerator();

    private static CreateOrganization ValidCommand(string name = "New Organization", string? description = null)
        => new(name, description);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_NameEmptyOrWhitespace_HasValidationErrorForName(string name)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand() with { Name = name };

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameExceedsMaxLength_HasValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand() with { Name = new string('a', 101) };

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameAtMaxLength_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand() with { Name = new string('a', 100) };

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_SlugAlreadyExistsGlobally_HasValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization existing = OrganizationBuilder.Create(name: "Existing Org", slug: "existing-org");
        db.Organizations.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand(name: "Existing Org");

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("An organization with this name already exists.");
    }

    [Fact]
    public async Task TestValidateAsync_NameGloballyUnique_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization existing = OrganizationBuilder.Create(name: "Existing Org", slug: "existing-org");
        db.Organizations.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand(name: "Brand New Org");

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_DescriptionExceedsMaxLength_HasValidationErrorForDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand(description: new string('a', 1001));

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public async Task TestValidateAsync_DescriptionAtMaxLength_HasNoValidationErrorForDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand(description: new string('a', 1000));

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public async Task TestValidateAsync_DescriptionNull_HasNoValidationErrorForDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateOrganizationValidator(db, SlugGenerator);
        CreateOrganization command = ValidCommand(description: null);

        TestValidationResult<CreateOrganization> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }
}
