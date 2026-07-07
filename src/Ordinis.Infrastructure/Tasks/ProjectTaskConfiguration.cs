using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordinis.Domain.Tasks;

namespace Ordinis.Infrastructure.Tasks;

/// <summary>
/// EF Core entity configuration for <see cref="ProjectTask"/>.
/// </summary>
internal sealed class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.ToTable("Tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        // Description supports markdown — no upper length cap at the DB level.
        builder.Property(t => t.Description);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.Priority)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // App-managed concurrency token (assigned by AppDbContext.SaveChangesAsync) — not
        // database-generated, so behavior is identical on SQL Server and PostgreSQL.
        builder.Property(t => t.RowVersion)
            .IsConcurrencyToken();

        builder.HasQueryFilter(t => !t.IsDeleted);

        builder.HasOne(t => t.Board)
            .WithMany()
            .HasForeignKey(t => t.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Reporter)
            .WithMany()
            .HasForeignKey(t => t.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        // AssigneeId is nullable — if the user record is ever hard-deleted,
        // tasks assigned to them become unassigned rather than being blocked.
        builder.HasOne(t => t.Assignee)
            .WithMany()
            .HasForeignKey(t => t.AssigneeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Comments)
            .WithOne(c => c.Task)
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Attachments)
            .WithOne(a => a.Task)
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
