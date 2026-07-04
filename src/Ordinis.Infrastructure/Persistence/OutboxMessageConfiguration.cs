using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ordinis.Infrastructure.Persistence;

/// <summary>
/// EF Core entity configuration for <see cref="OutboxMessage"/>.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.OccurredAt)
            .IsRequired();

        // Full CLR type name (e.g. "Ordinis.Domain.Tasks.TaskCreated") — used by
        // OutboxDispatcherJob to resolve the concrete type for deserialization.
        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(500);

        // JSON-serialized event payload. No upper length cap — event payloads
        // vary in size and constraining them risks truncation for complex events.
        builder.Property(m => m.Payload)
            .IsRequired();

        // Null until the dispatcher processes the row. Indexed to make the
        // "fetch all unprocessed messages" query efficient at scale.
        builder.Property(m => m.ProcessedAt);

        builder.HasIndex(m => m.ProcessedAt);
    }
}
