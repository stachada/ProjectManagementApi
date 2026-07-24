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
builder.Services.AddApiServices();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Middleware order: correlation ID -> request logging -> global exception -> concurrency token
// extraction -> routing -> auth (Phase 8) -> endpoints.
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
        StatusCodes.Status404NotFound => "Resource not found",
        StatusCodes.Status405MethodNotAllowed => "Method not allowed",
        _ => "Request failed",
    };

    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(
        ProblemDetailsFactory.Create(context, context.Response.StatusCode, title));
});

app.UseHttpsRedirection();
app.UseStaticFiles(); // serves wwwroot/attachents for LocalFileStorageService download URLs

app.UseCors();
app.UseRateLimiter();
app.UseResponseCaching();

app.MapHealthChecks("/health");
app.MapControllers();

app.MapSearchEndpoints();
app.MapAuthEndpoints();

app.Run();

/// <summary>
/// Exposes the top-level-statement entry point so <c>WebApplicationFactory&lt;Program&gt;</c>
/// can bootstrap this API in-process for integration tests.
/// </summary>
public partial class Program;
