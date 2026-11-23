using Ordinis.Api.Common;
using Ordinis.Api.MinimalApis;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Common;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    // ReadFrom.Services adds sinks and enrichers registered in DI (e.g. a test's log capture)
    // to the ones configured in appsettings.
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Middleware order: correlation ID -> request logging -> global exception -> concurrency token
// extraction -> status code pages -> CORS -> authentication -> rate limiting -> authorization ->
// static files -> idempotency replay -> response caching -> endpoints.
// Rate limiting sits between authentication (so per-user partitions see the caller) and
// authorization (so 401/403-rejected requests still consume quota). Static files, idempotency
// replay and response caching all sit after authorization so nothing is ever served to a caller
// who hasn't passed its policy (or the deny-by-default fallback policy).
// WebApplication's minimal hosting model auto-inserts UseRouting() as the very first pipeline
// entry whenever any endpoints are mapped, so context.GetEndpoint() is already resolved for
// every middleware below (IdempotencyMiddleware relies on it for the [Idempotent] attribute).
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<ConcurrencyTokenMiddleware>();


// Gives a Problem Details body to error status codes that reach the client without an
// exception ever being thrown - an unmatched route (404) or a disallowed HTTP method (405) -
// so every error response, not just handler-thrown ones, is shaped consistently.
app.UseStatusCodePages(async statusCodeContext =>
{
    HttpContext context = statusCodeContext.HttpContext;
    string title = context.Response.StatusCode switch
    {
        StatusCodes.Status401Unauthorized => "Authentication required",
        StatusCodes.Status403Forbidden => "Access denied",
        StatusCodes.Status404NotFound => "Resource not found",
        StatusCodes.Status405MethodNotAllowed => "Method not allowed",
        _ => "Request failed",
    };

    // contentType must be passed explicitly - WriteAsJsonAsync always sets Content-Type itself
    // (defaulting to "application/json"), overwriting any prior assignment to
    // context.Response.ContentType.
    await context.Response.WriteAsJsonAsync(
        ProblemDetailsFactory.Create(context, context.Response.StatusCode, title),
        options: null,
        contentType: "application/problem+json");
});

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.UseStaticFiles(); // serves wwwroot/attachents for LocalFileStorageService download URLs
app.UseMiddleware<IdempotencyMiddleware>();
app.UseResponseCaching();

// Liveness/readiness probes are polled frequently by orchestrators - exempt from rate limiting
// so a busy probe interval doesn't trip the same limiter guarding real traffic.
app.MapHealthChecks("/health").DisableRateLimiting().AllowAnonymous();
app.MapControllers();

app.MapSearchEndpoints();
app.MapAuthEndpoints();
app.MapWebhookEndpoints();

app.Run();

/// <summary>
/// Exposes the top-level-statement entry point so <c>WebApplicationFactory&lt;Program&gt;</c>
/// can bootstrap this API in-process for integration tests.
/// </summary>
public partial class Program;
