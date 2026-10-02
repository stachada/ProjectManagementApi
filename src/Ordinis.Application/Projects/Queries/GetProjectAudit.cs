using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Queries;

/// <summary>
/// Filter criteria for <see cref="GetProjectAudit"/>. All members are optional
/// </summary>
/// <param name="Page">Page number for pagination.</param>
/// <param name="PageSize">Number of items per page.</param>
public sealed record AuditFilter(
    int Page = 1,
    int PageSize = 20);

/// <summary>
/// Returns a paginated list of audit entries for a specific project.
/// </summary>
/// <param name="ProjectId">The ID of the project.</param>
/// <param name="Filter">Filter criteria for the audit entries.</param>
public sealed record GetProjectAudit(
    Guid ProjectId,
    AuditFilter? Filter = null) : IQuery<PagedResult<AuditEntryDto>>;

/// <summary>
/// Handles <see cref="GetProjectAudit"/>. Uses Dapper to query the <c>OutboxMessages</c> table
/// for audit entries related to tasks in the specified project. Returns a paginated list of
/// <see cref="AuditEntryDto"/> objects.
/// </summary>
/// <param name="db"></param>
public sealed class GetProjectAuditHandler(IAppDbContext db) : IQueryHandler<GetProjectAudit, PagedResult<AuditEntryDto>>
{
    public async Task<PagedResult<AuditEntryDto>> HandleAsync(
        GetProjectAudit query,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects
            .AnyAsync(p => p.Id == query.ProjectId, cancellationToken);

        if (!projectExists)
        {
            throw new NotFoundException(nameof(Project), query.ProjectId);
        }

        AuditFilter filter = query.Filter ?? new AuditFilter();
        var pageSize = Math.Min(filter.PageSize, 100);
        var page = Math.Max(filter.Page, 1);
        var skip = (page - 1) * pageSize;

        IDbConnection connection = db.GetDbConnection();

        // CommandDefinition carries the CancellationToken through to Dapper every other
        // handler in this codebase takes one, so this query honors it too.
        (var countSql, var pageSql) = db.DatabaseProvider switch
        {
            "SqlServer" => (CountSqlSqlServer, PageSqlSqlServer),
            "PostgreSQL" => (CountSqlPostgres, PageSqlPostgres),
            _ => throw new InvalidOperationException($"Unsupported database provider: {db.DatabaseProvider}")
        };

        var parameters = new { query.ProjectId, Skip = skip, Take = pageSize };

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        IEnumerable<AuditRow> rows = await connection.QueryAsync<AuditRow>(
            new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));

        var items = rows.Select(r => r.ToAuditEntryDto()).ToList();

        return new PagedResult<AuditEntryDto>(items, totalCount, page, pageSize);
    }

    // FIX: explicit column names, not SELECT *, so a Column-rename in OutboxMessageConfiguration
    // or ProjectTaskConfiguration/BoardConfiguration is forced to update this query too -
    // same discipline OutboxDispatcherJob.FetchBatchAsync already follows.
    private const string CountSqlSqlServer = """
        SELECT COUNT(*)
        FROM [OutboxMessages] o
        INNER JOIN [Tasks] t ON t.[Id] = TRY_CAST(JSON_VALUE(o.[Payload], '$.TaskId') AS uniqueidentifier)
        INNER JOIN [Boards] b ON b.[Id] = t.[BoardId]
        WHERE b.[ProjectId] = @ProjectId
          AND o.[Type] LIKE 'Ordinis.Domain.Tasks.%'
        """;

    private const string PageSqlSqlServer = """
        SELECT o.[Id], o.[Type], o.[OccurredAt], o.[Payload]
        FROM [OutboxMessages] o
        INNER JOIN [Tasks] t ON t.[Id] = TRY_CAST(JSON_VALUE(o.[Payload], '$.TaskId') AS uniqueidentifier)
        INNER JOIN [Boards] b ON b.[Id] = t.[BoardId]
        WHERE b.[ProjectId] = @ProjectId
          AND o.[Type] LIKE 'Ordinis.Domain.Tasks.%'
        ORDER BY o.[OccurredAt] DESC, o.[Id] DESC
        OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
        """;

    private const string CountSqlPostgres = """
        SELECT COUNT(*)
        FROM "OutboxMessages" o
        INNER JOIN "Tasks" t ON t."Id" = (o."Payload"::json->>'TaskId')::uuid
        INNER JOIN "Boards" b ON b."Id" = t."BoardId"
        WHERE b."ProjectId" = @ProjectId
          AND o."Type" LIKE 'Ordinis.Domain.Tasks.%'
        """;

    private const string PageSqlPostgres = """
        SELECT o."Id" AS "Id", o."Type" AS "Type", o."OccurredAt" AS "OccurredAt", o."Payload" AS "Payload"
        FROM "OutboxMessages" o
        INNER JOIN "Tasks" t ON t."Id" = (o."Payload"::json->>'TaskId')::uuid
        INNER JOIN "Boards" b ON b."Id" = t."BoardId"
        WHERE b."ProjectId" = @ProjectId
          AND o."Type" LIKE 'Ordinis.Domain.Tasks.%'
        ORDER BY o."OccurredAt" DESC, o."Id" DESC
        LIMIT @Take OFFSET @Skip
        """;
}
