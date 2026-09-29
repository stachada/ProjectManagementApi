using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// EF Core entity configuration for <see cref="WebhookDelivery"/>.
/// </summary>
internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.WebhookId)
            .IsRequired();

        builder.Property(d => d.Url)
            .IsRequired()
            .HasMaxLength(2000);

        // JSON envelope payload. No upper length cap, same rationale as OutboxMessage.Payload.
        builder.Property(d => d.Payload)
            .IsRequired();

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(d => d.AttemptCount)
            .HasDefaultValue(0);

        builder.Property(d => d.NextAttemptAt)
            .IsRequired();

        // Stores the last delivery error message; null on success.
        // Truncated to 2000 chars by WebhookDeliveryDispatcherService before assignment.
        builder.Property(d => d.LastError)
            .HasMaxLength(2000);

        // Supports WebhookDeliveryDispatcherService's "fetch due Pending deliveries" poll query.
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });

        // No FK to Webhooks: a delivery must survive its webhook being unregistered mid-retry
        // so the audit trail (including any eventual Failed outcome) is preserved. Url is
        // snapshotted at enqueue time for the same reason.
    }
}
