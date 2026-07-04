using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Tasks;

namespace Ordinis.Infrastructure.Tasks;

/// <summary>
/// EF Core entity configuration for <see cref="Comment"/>.
/// </summary>
internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Content)
            .IsRequired()
            .HasMaxLength(10_000);

        builder.Property(c => c.IsEdited)
            .HasDefaultValue(false);

        builder.HasQueryFilter(c => !c.IsDeleted);

        // Task FK is configured from ProjectTaskConfiguration.HasMany(Comments).
        // Only the Author FK needs explicit configuration here.
        builder.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
