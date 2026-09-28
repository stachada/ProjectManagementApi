using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Projects;

namespace Ordinis.Infrastructure.Projects;

/// <summary>
/// EF Core entity configuration for <see cref="Webhook"/>.
/// </summary>
/// <remarks>
/// <see cref="Webhook"/> is logically owned by the <see cref="Project"/> aggregate — it is
/// created and removed only through <c>Project.RegisterWebhook</c> / <c>Project.UnregisterWebhook</c>.
/// It has its own <c>DbSet</c> because <c>GetProjectWebhooksHandler</c> and
/// <c>WebhookDomainEventHandler</c> read it directly by FK without loading the full
/// <see cref="Project"/> aggregate — unlike <c>ProjectMember</c>, it uses its own <c>Id</c> as
/// the PK (not a composite key) since <c>DELETE .../webhooks/{webhookId}</c> addresses one
/// registration individually.
/// </remarks>
internal sealed class WebhookConfiguration : IEntityTypeConfiguration<Webhook>
{
    public void Configure(EntityTypeBuilder<Webhook> builder)
    {
        builder.ToTable("Webhooks");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.Url)
            .IsRequired()
            .HasMaxLength(2000);

        // Stored as a comma-joined string — no delimiter appears in a well-formed event name
        // (see WebhookEventTypes), so no escaping is needed. An explicit ValueComparer is
        // required for EF Core to detect in-place mutations of the underlying list correctly.
        builder.Property(w => w.EventTypes)
            .IsRequired()
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? Array.Empty<string>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
                c => c.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                c => c.ToList()));

        builder.Property(w => w.RegisteredByUserId)
            .IsRequired();

        builder.Property(w => w.RegisteredAt)
            .IsRequired();

        // Webhook has no soft-delete column of its own; chain the filter through the
        // required Project navigation so it always agrees with ProjectConfiguration's filter.
        builder.HasQueryFilter(w => !w.Project!.IsDeleted);

        builder.HasOne(w => w.Project)
            .WithMany(p => p.Webhooks)
            .HasForeignKey(w => w.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
