using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;

namespace Ordinis.Application.Tasks;

/// <summary>
/// Registers all Task-feature command and query handlers with the DI container.
/// Called from <c>ApplicationServiceExtensions.AddApplicationServices()</c>.
/// </summary>
internal static class TaskServiceExtensions
{
    /// <summary>
    /// Add all Task command handlers and query handlers as scoped services.
    /// </summary>
    /// <remarks>
    /// Validators are registered separately via FluentValidation's assembly scan
    /// in <c>AddApplicationServices</c> - they do not need to be listed here.
    /// </remarks>
    /// <param name="services"></param>
    /// <returns></returns>
    internal static IServiceCollection AddTaskHandlers(this IServiceCollection services)
    {
        // Commands
        services.AddScoped<ICommandHandler<CreateTask, Guid>, CreateTaskHandler>();
        services.AddScoped<ICommandHandler<UpdateTask>, UpdateTaskHandler>();
        services.AddScoped<ICommandHandler<DeleteTask>, DeleteTaskHandler>();
        services.AddScoped<ICommandHandler<MoveTask>, MoveTaskHandler>();
        services.AddScoped<ICommandHandler<AssignTask>, AssignTaskHandler>();
        services.AddScoped<ICommandHandler<UnassignTask>, UnassignTaskHandler>();

        services.AddScoped<ICommandHandler<AddComment, Guid>, AddCommentHandler>();
        services.AddScoped<ICommandHandler<EditComment>, EditCommentHandler>();
        services.AddScoped<ICommandHandler<RemoveComment>, RemoveCommentHandler>();

        services.AddScoped<ICommandHandler<AddAttachment, Guid>, AddAttachmentHandler>();
        services.AddScoped<ICommandHandler<RemoveAttachment>, RemoveAttachmentHandler>();

        // Queries
        services.AddScoped<IQueryHandler<GetTaskById, TaskDto>, GetTaskByIdHandler>();
        services.AddScoped<IQueryHandler<GetTasksFiltered, PagedResult<TaskSummaryDto>>, GetTasksFilteredHandler>();

        return services;
    }
}
