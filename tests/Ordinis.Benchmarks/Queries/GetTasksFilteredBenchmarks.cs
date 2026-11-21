using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Benchmarks.Infrastructure;
using Ordinis.Domain.Tasks;

namespace Ordinis.Benchmarks.Queries;

/// <summary>
/// The query shapes <see cref="GetTasksFilteredBenchmarks"/> measures.
/// </summary>
public enum TaskQueryScenario
{
    /// <summary>No filters, first page of 20, default <c>createdAt</c> sort - the default list view.</summary>
    FirstPage,

    /// <summary>Free-text search plus a priority filter, sorted by title - a typical search box query.</summary>
    SearchAndFilter,

    /// <summary>No filters, page 100 of 100-item pages (offset 9 900) - worst case for offset pagination.</summary>
    DeepPage,
}

/// <summary>
/// <c>GetTasksFiltered</c> against 10&#160;000 seeded tasks on SQL Server: the production EF Core
/// handler, an EF Core projection, and hand-written Dapper.
/// </summary>
/// <remarks>
/// <para>
/// Each operation opens a fresh DI scope - exactly what one HTTP request costs - so all three
/// variants pay for <c>AppDbContext</c> creation and connection-pool checkout alike.
/// </para>
/// <para>
/// <see cref="GlobalSetup"/> cross-checks every variant against the others before anything is
/// measured, so a variant can't "win" by returning less: total count and page membership/order
/// must be identical across all three, and the projection and Dapper DTOs must match field for
/// field. The production handler is exempt from the field-level check on
/// <c>CommentCount</c>/<c>AttachmentCount</c> only - it never loads attachments, so its
/// <c>AttachmentCount</c> is always 0 (a known defect, reported separately).
/// </para>
/// </remarks>
[Config(typeof(PerOperationLatencyConfig))]
public class GetTasksFilteredBenchmarks
{
    private ServiceProvider _services = null!;
    private ICurrentUserService _currentUser = null!;
    private TaskFilter _filter = null!;

    /// <summary>The query shape for this benchmark case.</summary>
    [Params(TaskQueryScenario.FirstPage, TaskQueryScenario.SearchAndFilter, TaskQueryScenario.DeepPage)]
    public TaskQueryScenario Scenario { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // BenchmarkDotNet setup methods are synchronous by contract; block once here, outside measurement.
        string connectionString = BenchmarkDatabase.GetConnectionStringAsync().GetAwaiter().GetResult();
        SeededDataset dataset = BenchmarkDatabase.PrepareAsync(connectionString).GetAwaiter().GetResult();

        _currentUser = new BenchmarkCurrentUser(dataset.AdminUserId, dataset.OrganizationId, IsOrgAdmin: true);
        _services = BenchmarkDatabase.BuildServices(connectionString, _currentUser);

        _filter = Scenario switch
        {
            TaskQueryScenario.FirstPage => new TaskFilter(),
            TaskQueryScenario.SearchAndFilter => new TaskFilter(Search: "billing", Priority: Priority.High, SortBy: "title"),
            TaskQueryScenario.DeepPage => new TaskFilter(Page: 100, PageSize: 100),
            _ => throw new ArgumentOutOfRangeException(nameof(Scenario), Scenario, null),
        };

        VerifyVariantsAgree();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _services.Dispose();

    /// <summary>The production <c>GetTasksFilteredHandler</c>, resolved from DI as the dispatcher would.</summary>
    [Benchmark(Baseline = true, Description = "EF Core (production handler)")]
    public async Task<PagedResult<TaskSummaryDto>> EfCoreHandler()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IQueryHandler<GetTasksFiltered, PagedResult<TaskSummaryDto>> handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetTasksFiltered, PagedResult<TaskSummaryDto>>>();

        return await handler.HandleAsync(new GetTasksFiltered(_filter), CancellationToken.None);
    }

    /// <summary>EF Core with a <c>Select</c> projection instead of <c>Include</c> + mapper.</summary>
    [Benchmark(Description = "EF Core (projection)")]
    public async Task<PagedResult<TaskSummaryDto>> EfCoreProjection()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        return await EfProjectionGetTasksFiltered.HandleAsync(db, _filter, _currentUser, CancellationToken.None);
    }

    /// <summary>Hand-written SQL via Dapper on the context's own connection.</summary>
    [Benchmark(Description = "Dapper")]
    public async Task<PagedResult<TaskSummaryDto>> Dapper()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        return await DapperGetTasksFiltered.HandleAsync(db.GetDbConnection(), _filter, _currentUser, CancellationToken.None);
    }

    private void VerifyVariantsAgree()
    {
        PagedResult<TaskSummaryDto> handler = EfCoreHandler().GetAwaiter().GetResult();
        PagedResult<TaskSummaryDto> projection = EfCoreProjection().GetAwaiter().GetResult();
        PagedResult<TaskSummaryDto> dapper = Dapper().GetAwaiter().GetResult();

        if (handler.Items.Count == 0)
        {
            throw new InvalidOperationException($"Scenario {Scenario} returned no rows - the benchmark would measure nothing.");
        }

        if (handler.TotalCount != projection.TotalCount || handler.TotalCount != dapper.TotalCount)
        {
            throw new InvalidOperationException(
                $"Scenario {Scenario}: TotalCount differs - handler {handler.TotalCount}, " +
                $"projection {projection.TotalCount}, Dapper {dapper.TotalCount}.");
        }

        Guid[] handlerIds = [.. handler.Items.Select(i => i.Id)];
        if (!handlerIds.SequenceEqual(projection.Items.Select(i => i.Id))
            || !handlerIds.SequenceEqual(dapper.Items.Select(i => i.Id)))
        {
            throw new InvalidOperationException($"Scenario {Scenario}: page membership or order differs between variants.");
        }

        string projectionJson = JsonSerializer.Serialize(projection.Items);
        string dapperJson = JsonSerializer.Serialize(dapper.Items);
        if (projectionJson != dapperJson)
        {
            throw new InvalidOperationException($"Scenario {Scenario}: projection and Dapper DTOs differ.");
        }

        // Everything except the two count fields must match the production handler too.
        static object WithoutCounts(TaskSummaryDto d) => new
        {
            d.Id,
            d.BoardId,
            d.Title,
            d.Status,
            d.Priority,
            d.AssigneeId,
            d.AssigneeDisplayName,
            d.DueDate,
            d.CreatedAt,
            d.UpdatedAt,
        };

        if (JsonSerializer.Serialize(handler.Items.Select(WithoutCounts))
            != JsonSerializer.Serialize(dapper.Items.Select(WithoutCounts)))
        {
            throw new InvalidOperationException($"Scenario {Scenario}: production handler DTOs differ from Dapper's.");
        }
    }
}
