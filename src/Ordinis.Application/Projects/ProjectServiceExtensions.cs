using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Application.Tasks.Dtos;

namespace Ordinis.Application.Projects;

/// <summary>
/// Registers all Project and Board command handlers, query handlers, and
/// related services with the DI container.
/// Called from <c>ApplicationServiceExtensions.AddApplicationServices()</c>.
/// All handlers are registered as <c>Scoped</c> to align with
/// <c>IAppDbContext</c> lifetime.
/// </summary>
public static class ProjectServiceExtensions
{
    public static IServiceCollection AddProjectHandlers(this IServiceCollection services)
    {
        // Project commands
        services.AddScoped<ICommandHandler<CreateProject, Guid>, CreateProjectHandler>();
        services.AddScoped<ICommandHandler<UpdateProject>, UpdateProjectHandler>();
        services.AddScoped<ICommandHandler<ArchiveProject>, ArchiveProjectHandler>();
        services.AddScoped<ICommandHandler<UnarchiveProject>, UnarchiveProjectHandler>();
        services.AddScoped<ICommandHandler<DeleteProject>, DeleteProjectHandler>();

        // Member commands
        services.AddScoped<ICommandHandler<AddProjectMember>, AddProjectMemberHandler>();
        services.AddScoped<ICommandHandler<RemoveProjectMember>, RemoveProjectMemberHandler>();
        services.AddScoped<ICommandHandler<ChangeMemberRole>, ChangeMemberRoleHandler>();

        // Board commands
        services.AddScoped<ICommandHandler<CreateBoard, Guid>, CreateBoardHandler>();
        services.AddScoped<ICommandHandler<ArchiveBoard>, ArchiveBoardHandler>();
        services.AddScoped<ICommandHandler<RenameBoard>, RenameBoardHandler>();

        // Project queries
        services.AddScoped<IQueryHandler<GetProjectById, ProjectDto>, GetProjectByIdHandler>();
        services.AddScoped<IQueryHandler<GetProjectsFiltered, PagedResult<ProjectSummaryDto>>, GetProjectsFilteredHandler>();
        services.AddScoped<IQueryHandler<GetProjectMembers, IReadOnlyList<ProjectMemberDto>>, GetProjectMembersHandler>();
        services.AddScoped<IQueryHandler<GetProjectTasks, PagedResult<TaskSummaryDto>>, GetProjectTasksHandler>();

        // Board queries
        services.AddScoped<IQueryHandler<GetBoardById, BoardDto>, GetBoardByIdHandler>();
        services.AddScoped<IQueryHandler<GetBoardTasks, PagedResult<TaskSummaryDto>>, GetBoardTasksHandler>();

        return services;
    }
}
