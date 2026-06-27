using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Application.Organizations.Dtos;
using Ordinis.Application.Organizations.Queries;
using Ordinis.Application.Projects.Dtos;

namespace Ordinis.Application.Organizations;

/// <summary>
/// DI registration for all Organization command and query handlers.
/// Called from <c>Program.cs</c> via <c>AddApplicationServices</c>.
/// </summary>
internal static class OrganizationServiceExtensions
{
    internal static IServiceCollection AddOrganizationHandlers(this IServiceCollection services)
    {
        // Commands
        services.AddScoped<ICommandHandler<CreateOrganization, Guid>, CreateOrganizationHandler>();
        services.AddScoped<ICommandHandler<RenameOrganization>, RenameOrganizationHandler>();
        services.AddScoped<ICommandHandler<UpdateOrganizationDescription>, UpdateOrganizationDescriptionHandler>();
        services.AddScoped<ICommandHandler<SuspendOrganization>, SuspendOrganizationHandler>();
        services.AddScoped<ICommandHandler<ReactivateOrganization>, ReactivateOrganizationHandler>();
        // Queries
        services.AddScoped<IQueryHandler<GetOrganizationById, OrganizationDto>, GetOrganizationByIdHandler>();
        services.AddScoped<IQueryHandler<GetOrganizationProjects, PagedResult<ProjectSummaryDto>>, GetOrganizationProjectsHandler>();

        return services;
    }
}
