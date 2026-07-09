using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Projects;

namespace Ordinis.Infrastructure.Projects;

/// <summary>
/// EF Core entity configuration for <see cref="ProjectMember"/>.
/// </summary>
/// <remarks>
/// <see cref="ProjectMember"/> is logically owned by the <see cref="Project"/> aggregate —
/// it is created and removed only through <c>Project.AddMember</c> / <c>Project.RemoveMember</c>.
/// It has its own <c>DbSet</c> because query handlers read it directly by FK without loading
/// the full <see cref="Project"/> aggregate.
/// </remarks>
internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers");

        // Composite PK — a user can only hold one role per project.
        builder.HasKey(m => new { m.ProjectId, m.UserId });

        // Entity.Id is present on the base class but is not the PK here;
        // mark it as never DB-generated so EF Core doesn't try to sequence it.
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(m => m.JoinedAt)
            .IsRequired();

        // ProjectMember has no soft-delete column of its own; chain the filter through the
        // required Project navigation so it always agrees with ProjectConfiguration's filter.
        builder.HasQueryFilter(m => !m.Project!.IsDeleted);

        // FK to Project is already configured via ProjectConfiguration.HasMany,
        // but restated here explicitly for readability.
        builder.HasOne(m => m.Project)
            .WithMany(p => p.Members)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
