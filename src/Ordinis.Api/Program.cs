using Microsoft.AspNetCore.RateLimiting;
using Ordinis.Api.Common;
using Ordinis.Api.MinimalApis;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Common;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
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
    app.MapOpenApi();
}

// Middleware order: correlation ID -> request logging -> global exception -> concurrency token
// extraction -> idempotency key replay -> routing -> auth (Phase 8) -> endpoints.
// IdempotencyMiddleware relies on context.GetEndpoint() to read the [Idempotent] attribute;
// WebApplication's minimal hosting model auto-inserts UseRouting() as the very first pipeline
// entry whenever any endpoints are mapped (before any app.UseMiddleware<> call here), so the
// endpoint is already resolved by the time this runs despite being registered ahead of
// MapControllers().
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<ConcurrencyTokenMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

// Gives a Problem Details body to error status codes that reach the client without an
// exception ever being thrown - an unmatched route (404) or a disallowed HTTP method (405) -
// so every error response, not just handler-thrown ones, is shaped consistently.
app.UseStatusCodePages(async statusCodeContext =>
{
    HttpContext context = statusCodeContext.HttpContext;
    string title = context.Response.StatusCode switch
    {
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
app.UseStaticFiles(); // serves wwwroot/attachents for LocalFileStorageService download URLs

app.UseCors();
app.UseRateLimiter();
app.UseResponseCaching();

// Liveness/readiness probes are polled frequently by orchestrators - exempt from rate limiting
// so a busy probe interval doesn't trip the same limiter guarding real traffic.
app.MapHealthChecks("/health").DisableRateLimiting();
app.MapControllers();

app.MapSearchEndpoints();
app.MapAuthEndpoints();

app.Run();

/// <summary>
/// Exposes the top-level-statement entry point so <c>WebApplicationFactory&lt;Program&gt;</c>
/// can bootstrap this API in-process for integration tests.
/// </summary>
public partial class Program;
