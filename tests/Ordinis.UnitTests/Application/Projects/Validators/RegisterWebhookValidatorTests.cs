using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="RegisterWebhookValidator"/> rules, including the async
/// project-existence and user-existence checks run against the database.
/// </summary>
public sealed class RegisterWebhookValidatorTests
{
    private static RegisterWebhook ValidCommand(Guid projectId, Guid registeredByUserId, IReadOnlyList<string>? eventTypes = null)
        => new(projectId, "https://example.com/hook", eventTypes ?? [], registeredByUserId, [1, 2, 3, 4]);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_ProjectDoesNotExist_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7(), user.Id));

        result.ShouldHaveValidationErrorFor(x => x.ProjectId)
            .WithErrorMessage("Project not found.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/hook")]
    public async Task TestValidateAsync_InvalidUrl_HasValidationErrorForUrl(string url)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());
        RegisterWebhook command = ValidCommand(project.Id, user.Id) with { Url = url };

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("http://example.com/hook")]
    public async Task TestValidateAsync_ValidUrlScheme_HasNoValidationErrorForUrl(string url)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());
        RegisterWebhook command = ValidCommand(project.Id, user.Id) with { Url = url };

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public async Task TestValidateAsync_UrlGuardDisallows_HasValidationErrorForUrl()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard(isAllowed: false));

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id));

        result.ShouldHaveValidationErrorFor(x => x.Url)
            .WithErrorMessage("Url resolves to a private, loopback, or otherwise disallowed network address.");
    }

    [Fact]
    public async Task TestValidateAsync_UnknownEventType_HasValidationErrorForEventTypes()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());
        RegisterWebhook command = ValidCommand(project.Id, user.Id, ["not.a.real.event"]);

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor("EventTypes[0]");
    }

    [Fact]
    public async Task TestValidateAsync_KnownEventTypes_HasNoValidationErrorForEventTypes()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());
        RegisterWebhook command = ValidCommand(
            project.Id, user.Id, [Ordinis.Application.Common.WebhookEventTypes.TaskMoved]);

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor("EventTypes[0]");
    }

    [Fact]
    public async Task TestValidateAsync_RegisteredByUserIdDoesNotExist_HasValidationErrorForRegisteredByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(
            ValidCommand(project.Id, Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.RegisteredByUserId)
            .WithErrorMessage("User not found.");
    }

    [Fact]
    public async Task TestValidateAsync_NullIfMatch_HasValidationErrorForIfMatch()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new RegisterWebhookValidator(db, new FakeWebhookUrlGuard());
        RegisterWebhook command = ValidCommand(project.Id, user.Id) with { IfMatch = null };

        TestValidationResult<RegisterWebhook> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }
}
