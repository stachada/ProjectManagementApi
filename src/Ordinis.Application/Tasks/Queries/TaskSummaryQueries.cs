using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Queries;

/// <summary>
/// Shared final step of every query that returns <see cref="TaskSummaryDto"/>s:
/// <see cref="GetTasksFilteredHandler"/>, <c>GetBoardTasksHandler</c>,
/// <c>GetProjectTasksHandler</c>, <c>GetUserTasksHandler</c> and <c>GetBoardByIdHandler</c>.
/// </summary>
internal static class TaskSummaryQueries
{
    /// <summary>
    /// Resolves assignee display names and live comment/attachment counts for an already
    /// filtered, sorted and paginated page of <paramref name="tasks"/>, and maps them to
    /// <see cref="TaskSummaryDto"/>s in the page's original order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One round-trip, scoped to the page's IDs: comment and attachment counts are SQL
    /// <c>COUNT</c> subqueries (the global soft-delete query filters still apply, so deleted
    /// comments aren't counted), and the display name is a correlated lookup on
    /// <c>Users</c> by FK - no cross-aggregate navigation.
    /// </para>
    /// <para>
    /// Running this <em>after</em> pagination, on at most one page of IDs, is deliberate. Put
    /// the same subqueries into the paged query itself and SQL Server evaluates them for every
    /// row it walks past to reach the offset - measured ~3-4x slower on a deep page in
    /// <c>GetTasksFilteredBenchmarks</c> (see docs/BENCHMARKS.md). It also replaces the
    /// handlers' previous separate assignee-name lookup, so the fix adds no round-trips.
    /// </para>
    /// <para>
    /// Local-environment note: a 100-ID page makes this request larger than one 8&#160;KB TDS
    /// packet. Through Docker Desktop's port proxy, such requests pay a fixed ~40&#160;ms
    /// (Nagle/delayed-ACK style) stall that a direct network path doesn't - so deep pages look
    /// slower against a Dockerized SQL Server on a dev machine than they are in production.
    /// </para>
    /// </remarks>
    public static async Task<List<TaskSummaryDto>> ToSummaryDtosAsync(
        this IReadOnlyList<ProjectTask> tasks,
        IAppDbContext db,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        Guid[] taskIds = [.. tasks.Select(t => t.Id)];

        // EF.Parameter sends the IDs as one JSON array that SQL Server expands with OPENJSON
        // (PostgreSQL: a single array parameter), instead of EF 10's default of one scalar
        // parameter per ID. Measured on a 100-ID page: ~2 ms vs ~8 ms server-side, and one
        // cached plan regardless of page size instead of one per parameter-count bucket.
        Dictionary<Guid, TaskSummaryDetails> details = await db.Tasks
            .Where(t => EF.Parameter(taskIds).Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                Details = new TaskSummaryDetails(
                    db.Users
                        .Where(u => u.Id == t.AssigneeId)
                        .Select(u => u.DisplayName)
                        .FirstOrDefault(),
                    t.Comments.Count(c => !c.IsDeleted),
                    t.Attachments.Count()),
            })
            .ToDictionaryAsync(x => x.Id, x => x.Details, cancellationToken);

        return tasks
            .Select(t => t.ToSummaryDto(details.GetValueOrDefault(t.Id, TaskSummaryDetails.None)))
            .ToList();
    }
}
