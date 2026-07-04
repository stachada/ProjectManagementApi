using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Organizations;

namespace Ordinis.Infrastructure.Organizations;

/// <summary>
/// EF Core entity configuration for <see cref="Organization"/>.
/// </summary>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(o => o.Slug)
            .IsUnique();

        builder.Property(o => o.Description)
            .HasMaxLength(1000);

        builder.Property(o => o.IsActive)
            .HasDefaultValue(true);

        builder.Property(o => o.RowVersion)
            .IsRowVersion();
    }
}
