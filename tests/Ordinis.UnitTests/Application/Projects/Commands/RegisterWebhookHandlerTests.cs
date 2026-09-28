using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class RegisterWebhookHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_RegistersWebhookAndReturnsItsId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var registeredByUserId = Guid.CreateVersion7();
        Guid webhookId = await new RegisterWebhookHandler(db, new FakeTimeProvider(Now))
            .HandleAsync(new RegisterWebhook(
                project.Id, "https://example.com/hook", [WebhookEventTypes.TaskMoved], registeredByUserId, project.RowVersion));

        Webhook webhook = await db.Webhooks.SingleAsync(w => w.Id == webhookId);
        Assert.Equal("https://example.com/hook", webhook.Url);
        Assert.Equal([WebhookEventTypes.TaskMoved], webhook.EventTypes);
        Assert.Equal(registeredByUserId, webhook.RegisteredByUserId);
        Assert.Equal(Now, webhook.RegisteredAt);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RegisterWebhookHandler(db, new FakeTimeProvider(Now))
                .HandleAsync(new RegisterWebhook(Guid.CreateVersion7(), "https://example.com/hook", [], Guid.CreateVersion7(), null)));
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        project.Archive();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new RegisterWebhookHandler(db, new FakeTimeProvider(Now))
                .HandleAsync(new RegisterWebhook(project.Id, "https://example.com/hook", [], Guid.CreateVersion7(), project.RowVersion)));
    }

    [Fact]
    public async Task HandleAsync_StaleIfMatch_ThrowsConcurrencyException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var staleToken = new byte[] { 9, 9, 9, 9 };
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            new RegisterWebhookHandler(db, new FakeTimeProvider(Now))
                .HandleAsync(new RegisterWebhook(project.Id, "https://example.com/hook", [], Guid.CreateVersion7(), staleToken)));
    }
}
