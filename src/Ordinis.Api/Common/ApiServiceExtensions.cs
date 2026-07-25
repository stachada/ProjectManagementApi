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
    /// Adds MVC controllers, response caching, CORS, and a global rate limiter (fixed window per
    /// IP for anonymous requests, sliding window per user for authenticated requests).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">
    /// Application configuration — used to bind <see cref="RateLimitingOptions"/> from the
    /// <c>RateLimiting</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
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

        var rateLimitingOptions = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.SectionName).Bind(rateLimitingOptions);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partitioned per client IP (anonymous) or per user ID (authenticated, once Phase 8
            // adds JWT auth) so one throttled caller doesn't affect others.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartitioner.CreatePartition(context, rateLimitingOptions));

            // Every other error response in this API is shaped as RFC 9457 Problem Details via
            // ProblemDetailsFactory (see GlobalExceptionMiddleware, Program.cs's
            // UseStatusCodePages callback) — mirror that here instead of letting AddRateLimiter's
            // default plain-text rejection body through.
            options.OnRejected = async (rejectedContext, cancellationToken) =>
            {
                HttpContext context = rejectedContext.HttpContext;

                if (rejectedContext.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                ProblemDetails problemDetails = ProblemDetailsFactory.Create(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests",
                    "Rate limit exceeded. Retry after the interval given in the Retry-After header.",
                    type: "https://httpstatuses.io/429");

                // WriteAsJsonAsync resets Content-Type to "application/json" unless the media
                // type is passed explicitly here — setting context.Response.ContentType beforehand
                // is not enough, it gets overwritten.
                await context.Response.WriteAsJsonAsync(
                    problemDetails,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
        });

        return services;
    }
}
