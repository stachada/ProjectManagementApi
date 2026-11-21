using System.Data;
using System.Text;
using Dapper;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Tasks;

namespace Ordinis.Benchmarks.Queries;

/// <summary>
/// Hand-written Dapper equivalent of <c>GetTasksFilteredHandler</c> (SQL Server dialect), used
/// only as the comparison point in <see cref="GetTasksFilteredBenchmarks"/>.
/// </summary>
/// <remarks>
/// <para>
/// Same semantics as the EF Core handler: identical optional filters, the same caller scoping
/// (org Admin sees every project in the org, anyone else only projects they're a member of),
/// the same soft-delete exclusions the global query filters apply, and the same
/// <c>sort key, Id</c> ordering <c>ThenByStableId</c> produces.
/// </para>
/// <para>
/// Where it differs is <em>shape</em>: comment/attachment counts are correlated
/// <c>COUNT(*)</c> subqueries evaluated only for the page's rows (a deferred join on the page's
/// IDs) and the assignee name is a <c>LEFT JOIN</c>, so no comment rows are transferred, and the
/// total count and the page travel in one round-trip via
/// <see cref="SqlMapper.QueryMultipleAsync(IDbConnection, CommandDefinition)"/>.
/// </para>
/// <para>
/// The dynamic SQL is built only from fixed fragments chosen by <c>switch</c>/<c>if</c>; every
/// caller-supplied value travels as a parameter, so it is not injectable.
/// </para>
/// </remarks>
internal static class DapperGetTasksFiltered
{
    private const int MaxPageSize = 100;

    public static async Task<PagedResult<TaskSummaryDto>> HandleAsync(
        IDbConnection connection,
        TaskFilter filter,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        int pageSize = Math.Min(filter.PageSize, MaxPageSize);
        int page = Math.Max(filter.Page, 1);

        var parameters = new DynamicParameters();
        parameters.Add("Skip", (page - 1) * pageSize);
        parameters.Add("Take", pageSize);

        string where = BuildWhereClause(filter, currentUser, parameters);
        string orderBy = BuildOrderByClause(filter);

        // Deferred join: the CTE finds the page's IDs using only the filter/sort columns, and the
        // correlated COUNT subqueries run for those <= 100 rows alone. Putting the subqueries
        // directly in a SELECT ... OFFSET makes SQL Server evaluate them for every row it walks
        // past to reach the offset - 10 000 rows instead of 100 on a deep page (measured ~4x slower).
        string sql = $"""
            SELECT COUNT(*)
            FROM [Tasks] t
            {where};

            WITH [Page] AS (
                SELECT t.[Id]
                FROM [Tasks] t
                {where}
                {orderBy}
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            )
            SELECT
                t.[Id], t.[BoardId], t.[Title], t.[Status], t.[Priority], t.[AssigneeId],
                u.[DisplayName] AS [AssigneeDisplayName],
                t.[DueDate], t.[CreatedAt], t.[UpdatedAt],
                (SELECT COUNT(*) FROM [Comments] c WHERE c.[TaskId] = t.[Id] AND c.[IsDeleted] = 0) AS [CommentCount],
                (SELECT COUNT(*) FROM [Attachments] a WHERE a.[TaskId] = t.[Id]) AS [AttachmentCount]
            FROM [Page] pg
            INNER JOIN [Tasks] t ON t.[Id] = pg.[Id]
            LEFT JOIN [Users] u ON u.[Id] = t.[AssigneeId]
            {orderBy};
            """;

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        int totalCount = await grid.ReadSingleAsync<int>();
        IEnumerable<TaskSummaryRow> rows = await grid.ReadAsync<TaskSummaryRow>();

        var items = rows.Select(r => r.ToDto()).ToList();
        return new PagedResult<TaskSummaryDto>(items, totalCount, page, pageSize);
    }

    private static string BuildWhereClause(TaskFilter filter, ICurrentUserService currentUser, DynamicParameters parameters)
    {
        var where = new StringBuilder("WHERE t.[IsDeleted] = 0");

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // EF Core escapes LIKE wildcards in Contains() parameters; mirror that so a search
            // for "50%" means the literal text, not "50 followed by anything".
            string escaped = filter.Search.Trim().ToLower()
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_")
                .Replace("[", @"\[");
            parameters.Add("Search", $"%{escaped}%");
            where.Append(@" AND (LOWER(t.[Title]) LIKE @Search ESCAPE '\' OR LOWER(t.[Description]) LIKE @Search ESCAPE '\')");
        }

        if (filter.BoardId.HasValue)
        {
            parameters.Add("BoardId", filter.BoardId.Value);
            where.Append(" AND t.[BoardId] = @BoardId");
        }

        if (filter.AssigneeId.HasValue)
        {
            parameters.Add("AssigneeId", filter.AssigneeId.Value);
            where.Append(" AND t.[AssigneeId] = @AssigneeId");
        }

        // Status and Priority are stored as strings (HasConversion<string>).
        if (filter.Status.HasValue)
        {
            parameters.Add("Status", filter.Status.Value.ToString(), DbType.String, size: 20);
            where.Append(" AND t.[Status] = @Status");
        }

        if (filter.Priority.HasValue)
        {
            parameters.Add("Priority", filter.Priority.Value.ToString(), DbType.String, size: 20);
            where.Append(" AND t.[Priority] = @Priority");
        }

        if (filter.DueBefore.HasValue)
        {
            parameters.Add("DueBefore", filter.DueBefore.Value);
            where.Append(" AND t.[DueDate] <= @DueBefore");
        }

        if (filter.DueAfter.HasValue)
        {
            parameters.Add("DueAfter", filter.DueAfter.Value);
            where.Append(" AND t.[DueDate] >= @DueAfter");
        }

        // Caller scoping - same rule as CurrentUserServiceExtensions.GetAllowedProjectIds, with
        // the Boards/Projects soft-delete filters EF Core would have applied spelled out.
        if (currentUser.IsOrgAdmin)
        {
            parameters.Add("OrganizationId", currentUser.OrganizationId);
            where.Append("""
                 AND EXISTS (
                    SELECT 1 FROM [Boards] b
                    INNER JOIN [Projects] p ON p.[Id] = b.[ProjectId]
                    WHERE b.[Id] = t.[BoardId] AND b.[IsDeleted] = 0
                      AND p.[OrganizationId] = @OrganizationId AND p.[IsDeleted] = 0)
                """);
        }
        else
        {
            parameters.Add("UserId", currentUser.UserId);
            where.Append("""
                 AND EXISTS (
                    SELECT 1 FROM [Boards] b
                    INNER JOIN [ProjectMembers] m ON m.[ProjectId] = b.[ProjectId]
                    INNER JOIN [Projects] p ON p.[Id] = m.[ProjectId]
                    WHERE b.[Id] = t.[BoardId] AND b.[IsDeleted] = 0
                      AND m.[UserId] = @UserId AND p.[IsDeleted] = 0)
                """);
        }

        return where.ToString();
    }

    private static string BuildOrderByClause(TaskFilter filter)
    {
        string column = filter.SortBy.ToLowerInvariant() switch
        {
            "title" => "t.[Title]",
            "status" => "t.[Status]",
            "priority" => "t.[Priority]",
            "duedate" => "t.[DueDate]",
            "updatedat" => "t.[UpdatedAt]",
            _ => "t.[CreatedAt]",
        };

        string direction = filter.SortDescending ? "DESC" : "ASC";

        // Id is always ascending - ThenByStableId never flips with the primary sort direction.
        return $"ORDER BY {column} {direction}, t.[Id] ASC";
    }

    /// <summary>Flat row shape Dapper materializes before mapping to the DTO.</summary>
    private sealed class TaskSummaryRow
    {
        public Guid Id { get; set; }
        public Guid BoardId { get; set; }
        public string Title { get; set; } = string.Empty;
        public ProjectTaskStatus Status { get; set; }
        public Priority Priority { get; set; }
        public Guid? AssigneeId { get; set; }
        public string? AssigneeDisplayName { get; set; }
        public DateTimeOffset? DueDate { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int CommentCount { get; set; }
        public int AttachmentCount { get; set; }

        public TaskSummaryDto ToDto() => new()
        {
            Id = Id,
            BoardId = BoardId,
            Title = Title,
            Status = Status,
            Priority = Priority,
            AssigneeId = AssigneeId,
            AssigneeDisplayName = AssigneeDisplayName,
            DueDate = DueDate,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CommentCount = CommentCount,
            AttachmentCount = AttachmentCount,
        };
    }
}
