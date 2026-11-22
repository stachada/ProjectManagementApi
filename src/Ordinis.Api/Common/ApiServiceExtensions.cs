using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Ordinis.Domain.Users;
using Ordinis.Infrastructure.Security;
using Ordinis.Infrastructure.Security.Authorization;

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

            // Partitioned per client IP (anonymous) or per user ID (authenticated, the JWT "sub"
            // claim) so one throttled caller doesn't affect others.
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

        IConfigurationSection jwtSection = configuration.GetSection("Jwt");
        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key 'Jwt:SigningKey'. " +
                "Set it via User Secrets (development) or environment variables (production).");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep claim types exactly as JwtTokenService issued them ("sub", "email", "org_id") -
                // policy handlers, CurrentUserService, RateLimitPartitioner and IdempotencyMiddleware all read "sub".
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSection["Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
                    ValidateLifetime = true,
                    // Default 5-minute slack lets an access token keep working briefly past its
                    // stated expiry on a slow/clock-skewed client - not appropriate for a 15-minute
                    // access token's security model, so it's removed.
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Name // so Identity.Name resolves (useful in logs)
                };

                // Default WWWW-Authenticate challenge response has no body. Give 401s the same
                // Problem Details shape as every other error response in this API.
                options.Events = new JwtBearerEvents
                {
                    // A signature-valid token is not enough: re-check the user's current state so a
                    // deactivated user is locked out and a role change applies immediately, not after
                    // the token expires. See ITokenPrincipalRevalidator.
                    OnTokenValidated = async context =>
                    {
                        ITokenPrincipalRevalidator revalidator =
                            context.HttpContext.RequestServices.GetRequiredService<ITokenPrincipalRevalidator>();

                        if (context.Principal is null
                            || !await revalidator.RevalidateAsync(context.Principal, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The user is unknown or no longer active.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(
                            ProblemDetailsFactory.Create(
                                context.HttpContext,
                                StatusCodes.Status401Unauthorized,
                                "Authentication required",
                                "A valid access token is required to access this resource."),
                            options: null,
                            contentType: "application/problem+json");
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.ContentType = "application/problem+json";
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(
                            ProblemDetailsFactory.Create(
                                context.HttpContext,
                                StatusCodes.Status403Forbidden,
                                "Access denied",
                                "You do not have permission to perform this action."),
                            options: null,
                            contentType: "application/problem+json");
                    }
                };
            });

        services.AddHttpContextAccessor();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("ProjectMember", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Project, "id")))
            .AddPolicy("ProjectAdmin", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Project, "id", Role.Admin)))
            .AddPolicy("BoardMember", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Board, "id")))
            .AddPolicy("BoardAdmin", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Board, "id", Role.Admin)))
            .AddPolicy("BoardCreateMember", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Project, "projectId", Role.Member)))
            .AddPolicy("TaskContributor", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Task, "id", Role.Member)))
            .AddPolicy("TaskMember", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Task, "id")))
            .AddPolicy("TaskProjectAdmin", p => p.Requirements.Add(
                new ProjectScopeRequirement(ProjectResourceType.Task, "id", Role.Admin)))
            .AddPolicy("TaskOwner", p => p.Requirements.Add(new TaskOwnerRequirement()))
            .AddPolicy("OrganizationMember", p => p.Requirements.Add(
                new OrganizationScopeRequirement(OrganizationResourceType.Organization, "id")))
            .AddPolicy("OrganizationAdmin", p => p.Requirements.Add(
                new OrganizationScopeRequirement(OrganizationResourceType.Organization, "id", requireOrgAdmin: true)))
            .AddPolicy("UserInOrganization", p => p.Requirements.Add(
                new OrganizationScopeRequirement(OrganizationResourceType.User, "id")))
            .AddPolicy("UserOrgAdmin", p => p.Requirements.Add(
                new OrganizationScopeRequirement(OrganizationResourceType.User, "id", requireOrgAdmin: true)))
            .AddPolicy("UserSelfOrOrgAdmin", p => p.Requirements.Add(
                new OrganizationScopeRequirement(OrganizationResourceType.User, "id", requireOrgAdmin: true, allowSelf: true)));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ResourceHidingAuthorizationResultHandler>();

        return services;
    }
}
