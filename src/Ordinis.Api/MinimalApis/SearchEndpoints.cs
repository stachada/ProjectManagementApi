using Ordinis.Api.Common;
using Ordinis.Api.Common.DataShaping;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;

namespace Ordinis.Api.MinimalApis;

/// <summary>
/// Registers the free-text search Minimal API endpoint.
/// </summary>
public static class SearchEndpoints
{
    /// <summary>
    /// Maps <c>GET /api/v1/search</c>, which delegates to <see cref="GetTasksFiltered"/> or
    /// <see cref="GetProjectsFiltered"/> depending on the <c>type</c> query parameter.
    /// </summary>
    /// <param name="routes">The endpoint route builder to register the endpoint on.</param>
    /// <returns>The same <paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/search",
            async (
                string? q,
                string? type,
                IDispatcher dispatcher,
                HttpContext context,
                int page = 1,
                int pageSize = 20,
                string? fields = null,
                CancellationToken ct = default) =>
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return Results.Problem(ProblemDetailsFactory.Create(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Missing query parameter",
                        "Query parameter 'q' is required."));
                }

                if (string.Equals(type, "tasks", StringComparison.OrdinalIgnoreCase))
                {
                    var getTasksFiltered = new GetTasksFiltered(new TaskFilter(Search: q, Page: page, PageSize: pageSize));
                    PagedResult<TaskSummaryDto> result = await dispatcher.QueryAsync<GetTasksFiltered, PagedResult<TaskSummaryDto>>(getTasksFiltered, ct);

                    context.Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());
                    return Results.Ok(DataShaper.ShapeCollection(result.Items, fields));
                }

                if (string.Equals(type, "projects", StringComparison.OrdinalIgnoreCase))
                {
                    var filter = new ProjectFilter
                    {
                        Search = q,
                        Page = page,
                        PageSize = pageSize
                    };
                    var getProjectsFiltered = new GetProjectsFiltered(filter);
                    PagedResult<ProjectSummaryDto> result = await dispatcher.QueryAsync<GetProjectsFiltered, PagedResult<ProjectSummaryDto>>(getProjectsFiltered, ct);

                    context.Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());
                    return Results.Ok(DataShaper.ShapeCollection(result.Items, fields));
                }

                return Results.Problem(ProblemDetailsFactory.Create(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Invalid query parameter",
                    "Query parameter 'type' must be either 'tasks' or 'projects'."));
            })
            .Produces<IReadOnlyList<TaskSummaryDto>>(StatusCodes.Status200OK)
            .Produces<IReadOnlyList<ProjectSummaryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return routes;
    }
}
