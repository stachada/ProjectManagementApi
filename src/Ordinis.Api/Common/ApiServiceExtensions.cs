using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ordinis.Api.Common;

/// <summary>
/// Registers API-layer (composition root) services with the DI container.
/// Called from <c>Ordinis.Api/Program.cs</c>.
/// </summary>
public static class ApiServiceExtensions
{
    /// <summary>
    /// Adds MVC controllers, response caching, CORS, and a global fixed-window rate limiter.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                // Default behavior returns ASP.NET Core's own ValidationProblemDetails shape,
                // bypassing ProblemDetailsFactory entirely (no correlationId, a generic RFC 9110
                // "type" URI instead of this API's https://httpstatuses.io/{status} convention).
                // Route it through the same factory every other error response uses.
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value is { Errors.Count: > 0 })
                        .ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

                    ValidationProblemDetails problemDetails =
                        ProblemDetailsFactory.CreateModelBindingValidation(context.HttpContext, errors);

                    return new ObjectResult(problemDetails)
                    {
                        StatusCode = problemDetails.Status,
                        ContentTypes = { "application/problem+json" },
                    };
                };
            });

        services.AddResponseCaching();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partitioned per client IP so one caller being throttled doesn't affect others.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
