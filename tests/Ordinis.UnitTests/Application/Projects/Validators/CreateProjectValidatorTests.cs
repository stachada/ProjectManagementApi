using FluentValidation.TestHelper;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="CreateProjectValidator"/> rules, including the async
/// organization-existence and slug-uniqueness checks run against the database.
/// </summary>
public sealed class CreateProjectValidatorTests
{
    private static readonly ISlugGenerator SlugGenerator = new SlugGenerator();

    private static CreateProject ValidCommand(
        Guid organizationId,
        string name = "New Project",
        string? description = null)
        => new(organizationId, Guid.CreateVersion7(), name, description);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(ValidCommand(organization.Id));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyOrganizationId_HasValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateProjectValidator(db, SlugGenerator);

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public async Task TestValidateAsync_OrganizationDoesNotExist_HasValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateProjectValidator(db, SlugGenerator);

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId)
            .WithErrorMessage("Organization does not exist or is inactive.");
    }

    [Fact]
    public async Task TestValidateAsync_OrganizationExists_HasNoValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(ValidCommand(organization.Id));

        result.ShouldNotHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public async Task TestValidateAsync_CreatedByUserIdEmpty_HasValidationErrorForCreatedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id) with { CreatedByUserId = Guid.Empty };

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.CreatedByUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_NameEmptyOrWhitespace_HasValidationErrorForName(string name)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id) with { Name = name };

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameExceedsMaxLength_HasValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id) with { Name = new string('a', 101) };

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameAtMaxLength_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id) with { Name = new string('a', 100) };

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_SlugAlreadyExistsInSameOrganization_HasValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        Project existing = ProjectBuilder.Create(
            organizationId: organization.Id,
            name: "Existing Project",
            slug: "existing-project");
        db.Organizations.Add(organization);
        db.Projects.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id, name: "Existing Project");

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("A project with the same name already exists in this organization.");
    }

    [Fact]
    public async Task TestValidateAsync_SlugAlreadyExistsInDifferentOrganization_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        Organization otherOrganization = OrganizationBuilder.Create(name: "Other Org", slug: "other-org");
        Project existing = ProjectBuilder.Create(
            organizationId: otherOrganization.Id,
            name: "Existing Project",
            slug: "existing-project");
        db.Organizations.Add(organization);
        db.Organizations.Add(otherOrganization);
        db.Projects.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id, name: "Existing Project");

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameUniqueWithinOrganization_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id, name: "Brand New Project");

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_DescriptionExceedsMaxLength_HasValidationErrorForDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id, description: new string('a', 1001));

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public async Task TestValidateAsync_DescriptionAtMaxLength_HasNoValidationErrorForDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id, description: new string('a', 1000));

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public async Task TestValidateAsync_DescriptionNull_HasNoValidationErrorForDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateProjectValidator(db, SlugGenerator);
        CreateProject command = ValidCommand(organization.Id, description: null);

        TestValidationResult<CreateProject> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }
}
