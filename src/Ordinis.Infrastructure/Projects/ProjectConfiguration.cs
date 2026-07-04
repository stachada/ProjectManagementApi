using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Projects;

namespace Ordinis.Infrastructure.Projects;

/// <summary>
/// EF Core entity configuration for <see cref="Project"/>.
/// </summary>
internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Slug)
            .IsRequired()
            .HasMaxLength(100);

        // Slug uniqueness is scoped per organization — two different orgs may share the same slug.
        builder.HasIndex(p => new { p.OrganizationId, p.Slug })
            .IsUnique();

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.IsArchived)
            .HasDefaultValue(false);

        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.HasOne(p => p.Organization)         // a project belongs to an organization
            .WithMany()                             // an organization may have many projects
            .HasForeignKey(p => p.OrganizationId)   // foreign key property on Project
            .OnDelete(DeleteBehavior.Restrict);     // can't delete an org or user while projects reference them

        builder.HasOne(p => p.CreatedByUser)        // a project was created by a user
            .WithMany()                             // a user may have created many projects
            .HasForeignKey(p => p.CreatedByUserId)  // foreign key property on Project
            .OnDelete(DeleteBehavior.Restrict);     // can't delete an org or user while projects reference them

        builder.HasMany(p => p.Members)             // a project may have many members
            .WithOne(m => m.Project)                // a membership row belongs to a project
            .HasForeignKey(m => m.ProjectId)        // foreign key property on ProjectMembership
            .OnDelete(DeleteBehavior.Cascade);      // removing a project cascades to its membership rows
    }
}
