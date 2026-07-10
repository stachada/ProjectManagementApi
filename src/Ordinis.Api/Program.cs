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

// Middleware order: correlation ID -> request logging -> global exception -> routing -> auth (Phase 8) -> endpoints.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

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
