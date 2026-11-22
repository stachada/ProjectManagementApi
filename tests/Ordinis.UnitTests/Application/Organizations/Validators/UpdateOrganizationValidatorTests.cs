using FluentValidation.TestHelper;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.UnitTests.Common;

namespace Ordinis.UnitTests.Application.Organizations.Validators;

/// <summary>
/// Verifies <see cref="UpdateOrganizationValidator"/> rules, including the async
/// name-clash check run against the database.
/// </summary>
public sealed class UpdateOrganizationValidatorTests
{
    private static readonly ISlugGenerator SlugGenerator = new SlugGenerator();

    private static UpdateOrganization ValidCommand(
        Guid? organizationId = null, string newName = "New Name", string? newDescription = "Updated description")
        => new(organizationId ?? Guid.CreateVersion7(), newName, newDescription);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_NullDescription_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newDescription: null));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyOrganizationId_HasValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(organizationId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_NewNameEmptyOrWhitespace_HasValidationErrorForNewName(string newName)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: newName));

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameExceedsMaxLength_HasValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: new string('a', 101)));

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameAtMaxLength_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: new string('a', 100)));

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewDescriptionExceedsMaxLength_HasValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newDescription: new string('a', 1001)));

        result.ShouldHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public async Task TestValidateAsync_NewDescriptionAtMaxLength_HasNoValidationErrorForNewDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newDescription: new string('a', 1000)));

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }


    // GAP-10: organization names are globally unique, on rename as on creation.
    [Theory]
    [InlineData("Globex", true)]
    [InlineData("globex", true)]
    [InlineData("ACME", false)]   // its own name in other casing
    [InlineData("Initech", false)]
    public async Task TestValidateAsync_RenameClash_DecidesValidity(string newName, bool expectError)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Domain.Organizations.Organization acme = Domain.Organizations.Organization.Create("Acme", "acme");
        db.Organizations.AddRange(acme, Domain.Organizations.Organization.Create("Globex", "globex"));
        await db.SaveChangesAsync();
        var validator = new UpdateOrganizationValidator(db, SlugGenerator);

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(acme.Id, newName));

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
