using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Projects;

namespace Ordinis.Infrastructure.Projects;

/// <summary>
/// EF Core entity configuration for <see cref="Board"/>.
/// </summary>
internal sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Boards");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(b => b.Description)
            .HasMaxLength(1000);

        builder.Property(b => b.IsArchived)
            .HasDefaultValue(false);

        // App-managed concurrency token (assigned by AppDbContext.SaveChangesAsync) — not
        // database-generated, so behavior is identical on SQL Server and PostgreSQL.
        builder.Property(b => b.RowVersion)
            .IsConcurrencyToken();

        builder.HasQueryFilter(b => !b.IsDeleted);

        builder.HasOne(b => b.Project)
            .WithMany()
            .HasForeignKey(b => b.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.CreatedByUser)
            .WithMany()
            .HasForeignKey(b => b.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
