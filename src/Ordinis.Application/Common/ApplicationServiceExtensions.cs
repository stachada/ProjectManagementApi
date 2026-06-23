using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Ordinis.Application.Common;

/// <summary>
/// Register all Application layer services with the DI container.
/// Called from <c>Ordinis.Api/Program.cs</c>.
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Adds CQRS dispatcher, all command handlers, all query handlers,
    /// and all FluentValidation validators from this assembly.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Dispatcher - scoped so it shares the same DI scope as handlers
        services.AddScoped<IDispatcher, Dispatcher>();

        // Auto-register all validators in this assembly.
        // AssemblyScanner finds every IValidator<T> implementation
        // and registers it with DI.
        services.AddValidatorsFromAssemblyContaining<ApplicationAssemblyMarker>(
            lifetime: ServiceLifetime.Scoped,
            includeInternalTypes: true);

        // Individual handlers are registered in the feature folders
        // by calling the static Register() extension from each feature
        // See Tasks/Commands/ and Tasks/Queries/ for examples.
        //
        // Handlers are added here as phase progress:
        //  services.AddTaskHandlers();
        //  services.AddProjectHandlers();
        //  etc.

        return services;
    }
}
