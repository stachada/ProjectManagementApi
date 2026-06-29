using FluentValidation.TestHelper;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Validators;

/// <summary>
/// Verifies <see cref="CreateUserValidator"/> rules, including the async
/// organization-existence/active check and the email-uniqueness check scoped
/// to the organization.
/// </summary>
public sealed class CreateUserValidatorTests
{
    private static CreateUser ValidCommand(
        Guid organizationId,
        string displayName = "New User",
        string email = "new.user@example.com",
        string password = "password123",
        Role orgRole = Role.Member)
        => new(organizationId, displayName, email, password, orgRole);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(ValidCommand(organization.Id));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyOrganizationId_HasValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateUserValidator(db);

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public async Task TestValidateAsync_OrganizationDoesNotExist_HasValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateUserValidator(db);

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId)
            .WithErrorMessage("Organization does not exist or is suspended.");
    }

    [Fact]
    public async Task TestValidateAsync_OrganizationSuspended_HasValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        organization.Suspend();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(ValidCommand(organization.Id));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId)
            .WithErrorMessage("Organization does not exist or is suspended.");
    }

    [Fact]
    public async Task TestValidateAsync_OrganizationExistsAndActive_HasNoValidationErrorForOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(ValidCommand(organization.Id));

        result.ShouldNotHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_DisplayNameEmptyOrWhitespace_HasValidationErrorForDisplayName(string displayName)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { DisplayName = displayName };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public async Task TestValidateAsync_DisplayNameExceedsMaxLength_HasValidationErrorForDisplayName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { DisplayName = new string('a', 101) };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public async Task TestValidateAsync_DisplayNameAtMaxLength_HasNoValidationErrorForDisplayName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { DisplayName = new string('a', 100) };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_EmailEmptyOrWhitespace_HasValidationErrorForEmail(string email)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { Email = email };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public async Task TestValidateAsync_EmailNotValidFormat_HasValidationErrorForEmail()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { Email = "not-an-email" };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public async Task TestValidateAsync_EmailExceedsMaxLength_HasValidationErrorForEmail()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        var localPart = new string('a', 250);
        CreateUser command = ValidCommand(organization.Id) with { Email = $"{localPart}@example.com" };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public async Task TestValidateAsync_EmailAlreadyExistsInSameOrganization_HasValidationErrorForEmail()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        User existing = UserBuilder.Create(organizationId: organization.Id, email: "taken@example.com");
        db.Organizations.Add(organization);
        db.Users.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id, email: "taken@example.com");

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("A user with this email already exists in the organization.");
    }

    [Fact]
    public async Task TestValidateAsync_EmailAlreadyExistsDifferentCasing_HasValidationErrorForEmail()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        User existing = UserBuilder.Create(organizationId: organization.Id, email: "taken@example.com");
        db.Organizations.Add(organization);
        db.Users.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id, email: "TAKEN@EXAMPLE.COM");

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("A user with this email already exists in the organization.");
    }

    [Fact]
    public async Task TestValidateAsync_EmailExistsInDifferentOrganization_HasNoValidationErrorForEmail()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        Organization otherOrganization = OrganizationBuilder.Create(name: "Other Org", slug: "other-org");
        User existing = UserBuilder.Create(organizationId: otherOrganization.Id, email: "taken@example.com");
        db.Organizations.Add(organization);
        db.Organizations.Add(otherOrganization);
        db.Users.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id, email: "taken@example.com");

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public async Task TestValidateAsync_EmailUniqueWithinOrganization_HasNoValidationErrorForEmail()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id, email: "fresh@example.com");

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1")]
    public async Task TestValidateAsync_PasswordEmptyOrTooShort_HasValidationErrorForPassword(string password)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { Password = password };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public async Task TestValidateAsync_PasswordAtMinimumLength_HasNoValidationErrorForPassword()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { Password = "12345678" };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public async Task TestValidateAsync_OrgRoleNotAValidEnumValue_HasValidationErrorForOrgRole()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { OrgRole = (Role)99 };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.OrgRole);
    }

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Member)]
    [InlineData(Role.Admin)]
    public async Task TestValidateAsync_OrgRoleIsValidEnumValue_HasNoValidationErrorForOrgRole(Role role)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization organization = OrganizationBuilder.Create();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var validator = new CreateUserValidator(db);
        CreateUser command = ValidCommand(organization.Id) with { OrgRole = role };

        TestValidationResult<CreateUser> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.OrgRole);
    }
}
