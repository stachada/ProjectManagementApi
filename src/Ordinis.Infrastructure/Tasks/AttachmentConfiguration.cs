using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Tasks;

namespace Ordinis.Infrastructure.Tasks;

/// <summary>
/// EF Core entity configuration for <see cref="Attachment"/>.
/// </summary>
/// <remarks>
/// <see cref="Attachment"/> derives from <see cref="Ordinis.Domain.Common.Entity"/>,
/// not <c>AuditableEntity</c> — it is append-only and has no soft-delete or
/// <c>CreatedAt</c>/<c>UpdatedAt</c> columns. <see cref="Attachment.UploadedAt"/>
/// is the single timestamp, set at creation time by the command handler.
/// </remarks>
internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.SizeInBytes)
            .IsRequired();

        builder.Property(a => a.StorageUrl)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(a => a.UploadedAt)
            .IsRequired();

        // Attachment has no soft-delete column of its own; chain the filter through the
        // required Task navigation so it always agrees with ProjectTaskConfiguration's filter.
        builder.HasQueryFilter(a => !a.Task!.IsDeleted);

        // Task FK is configured from ProjectTaskConfiguration.HasMany(Attachments).
        // Only the UploadedByUser FK needs explicit configuration here.
        builder.HasOne(a => a.UploadedByUser)
            .WithMany()
            .HasForeignKey(a => a.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
