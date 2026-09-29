using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class UnregisterWebhookHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_RegisteredWebhook_RemovesWebhook()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        Webhook webhook = project.RegisterWebhook("https://example.com/hook", [], Guid.CreateVersion7(), Now);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await new UnregisterWebhookHandler(db)
            .HandleAsync(new UnregisterWebhook(project.Id, webhook.Id, project.RowVersion));

        bool stillExists = await db.Webhooks.AnyAsync(w => w.Id == webhook.Id);
        Assert.False(stillExists);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UnregisterWebhookHandler(db)
                .HandleAsync(new UnregisterWebhook(Guid.CreateVersion7(), Guid.CreateVersion7(), null)));
    }

    [Fact]
    public async Task HandleAsync_WebhookNotRegistered_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new UnregisterWebhookHandler(db)
                .HandleAsync(new UnregisterWebhook(project.Id, Guid.CreateVersion7(), project.RowVersion)));
    }

    [Fact]
    public async Task HandleAsync_ArchivedProject_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        Webhook webhook = project.RegisterWebhook("https://example.com/hook", [], Guid.CreateVersion7(), Now);
        project.Archive();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new UnregisterWebhookHandler(db)
                .HandleAsync(new UnregisterWebhook(project.Id, webhook.Id, project.RowVersion)));
    }
}
