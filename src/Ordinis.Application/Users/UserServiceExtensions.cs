using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Users.Commands;
using Ordinis.Application.Users.Dtos;
using Ordinis.Application.Users.Queries;

namespace Ordinis.Application.Users;

/// <summary>
/// Registers all User feature handlers with the DI container.
/// Called by <c>ApplicationServiceExtensions.AddApplicationServices()</c>.
/// </summary>
public static class UserServiceExtensions
{
    /// <summary>
/// Adds all User command and query handlers as scoped services.
/// </summary>
public static IServiceCollection AddUserHandlers(this IServiceCollection services)
{
    // Commands
    services.AddScoped<ICommandHandler<CreateUser, Guid>, CreateUserHandler>();
    services.AddScoped<ICommandHandler<UpdateUser>, UpdateUserHandler>();
    services.AddScoped<ICommandHandler<DeactivateUser>, DeactivateUserHandler>();
    services.AddScoped<ICommandHandler<ReactivateUser>, ReactivateUserHandler>();
    services.AddScoped<ICommandHandler<ChangeUserOrgRole>, ChangeUserOrgRoleHandler>();

    // Queries
    services.AddScoped<IQueryHandler<GetUserById, UserDto>, GetUserByIdHandler>();
    services.AddScoped<IQueryHandler<GetUserTasks, PagedResult<TaskSummaryDto>>, GetUserTasksHandler>();

    return services;
}
}
