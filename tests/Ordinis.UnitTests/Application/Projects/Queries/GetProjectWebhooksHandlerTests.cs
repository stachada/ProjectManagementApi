using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetProjectWebhooksHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsWebhooksOrderedByRegisteredAt()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        project.RegisterWebhook("https://example.com/second", [], Guid.CreateVersion7(), Now.AddHours(1));
        project.RegisterWebhook("https://example.com/first", [], Guid.CreateVersion7(), Now);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        IReadOnlyList<WebhookDto> webhooks = await new GetProjectWebhooksHandler(db)
            .HandleAsync(new GetProjectWebhooks(project.Id));

        Assert.Equal(2, webhooks.Count);
        Assert.Equal("https://example.com/first", webhooks[0].Url);
        Assert.Equal("https://example.com/second", webhooks[1].Url);
    }

    [Fact]
    public async Task HandleAsync_NoWebhooksRegistered_ReturnsEmptyList()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        IReadOnlyList<WebhookDto> webhooks = await new GetProjectWebhooksHandler(db)
            .HandleAsync(new GetProjectWebhooks(project.Id));

        Assert.Empty(webhooks);
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetProjectWebhooksHandler(db)
                .HandleAsync(new GetProjectWebhooks(Guid.CreateVersion7())));
    }
}
