using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Users;

/// <summary>
/// EF Core entity configuration for <see cref="User"/>.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        // Email must be unique within an organization — not globally.
        // The composite index enforces this at the DB level; the validator
        // enforces it at the application level with a cleaner error message.
        builder.HasIndex(u => new { u.OrganizationId, u.Email })
            .IsUnique();

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(u => u.OrgRole)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true);

        builder.Property(u => u.RefreshToken)
            .HasMaxLength(500);

        // App-managed concurrency token (assigned by AppDbContext.SaveChangesAsync) — not
        // database-generated, so behavior is identical on SQL Server and PostgreSQL.
        builder.Property(u => u.RowVersion)
            .IsConcurrencyToken();

        builder.HasOne(u => u.Organization)
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
