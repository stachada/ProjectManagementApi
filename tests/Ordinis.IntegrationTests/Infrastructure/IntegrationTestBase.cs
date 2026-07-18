using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Users;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for API-level integration tests. Shares one <see cref="OrdinisApiFactory"/> (and
/// its SQL Server container) across the whole <see cref="ApiCollection"/>, and resets table data
/// after every test so tests remain independent regardless of execution order.
/// </summary>
[Collection(ApiCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected IntegrationTestBase(OrdinisApiFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected OrdinisApiFactory Factory { get; }

    protected HttpClient Client { get; }

    /// <summary>Opens a DI scope for seeding data directly via <c>AppDbContext</c> before a request is made.</summary>
    protected IServiceScope CreateScope() => Factory.Services.CreateScope();

    /// <summary>
    /// Opens a scope, resolves <c>AppDbContext</c>, runs <paramref name="seed"/> against it, and
    /// disposes the scope - the boilerplate every seeding helper otherwise repeats.
    /// </summary>
    protected async Task<TResult> SeedAsync<TResult>(Func<AppDbContext, Task<TResult>> seed)
    {
        using IServiceScope scope = CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await seed(db);
    }

    /// <summary>
    /// Builds an unsaved <see cref="Organization"/> with a globally-unique slug
    /// (<c>Organization.Slug</c> has a unique DB index) - callers still add it to
    /// <c>db.Organizations</c> and call <c>SaveChangesAsync</c>.
    /// </summary>
    protected static Organization CreateOrganization(string name = "Acme") =>
        Organization.Create(name, $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.CreateVersion7()}");

    /// <summary>
    /// Seeds a fresh <see cref="Organization"/> plus one <see cref="User"/> in it - the
    /// prerequisite pair nearly every controller test needs before it can seed anything more
    /// specific (a project, a board, a task). Each call creates its own organization (globally
    /// unique slug via <see cref="CreateOrganization"/>), so the email is never at risk of
    /// colliding with another call even without its own random suffix.
    /// </summary>
    protected Task<(Guid OrganizationId, Guid UserId)> SeedOrganizationWithUserAsync(
        string displayName = "Alice", string email = "alice@example.com") => SeedAsync(async db =>
    {
        Organization org = CreateOrganization();
        db.Organizations.Add(org);

        User user = User.Create(org.Id, displayName, email, "hashed-password");
        db.Users.Add(user);

        await db.SaveChangesAsync();

        return (org.Id, user.Id);
    });

    /// <summary>
    /// Deterministically triggers an optimistic-concurrency conflict: fires
    /// <paramref name="sendFirstRequest"/>, waits for it to pause mid-save via
    /// <see cref="ConcurrencyRaceInterceptor"/>, then fully runs <paramref name="sendSecondRequest"/>
    /// to completion (it loads the pre-conflict state and saves normally), then releases the first
    /// request so its save observes a stale RowVersion. Asserts the first request gets
    /// <c>409 Conflict</c> and the second gets <c>204 No Content</c> - the only way this API (no
    /// ETag/If-Match at the HTTP layer) can produce a genuine optimistic-concurrency conflict.
    /// See docs/INTEGRATION_TESTS.md for the full mechanism.
    /// </summary>
    protected async Task AssertConcurrentRequestsConflictAsync(
        Func<Task<HttpResponseMessage>> sendFirstRequest,
        Func<Task<HttpResponseMessage>> sendSecondRequest)
    {
        ConcurrencyRaceInterceptor interceptor = Factory.Services.GetRequiredService<ConcurrencyRaceInterceptor>();
        interceptor.Arm();

        // fire and forget the first request, which will pause mid-save in the interceptor
        Task<HttpResponseMessage> firstResponseTask = sendFirstRequest();
        // Bounded wait rather than an unbounded await: if the interceptor mechanism ever
        // regresses (e.g. stops being attached to AppDbContext), this fails fast with a clear
        // timeout instead of hanging the test run forever.
        await interceptor.WaitForFirstArrivalAsync().WaitAsync(TimeSpan.FromSeconds(15));

        HttpResponseMessage secondResponse = await sendSecondRequest().WaitAsync(TimeSpan.FromSeconds(15));
        interceptor.ReleaseFirst();
        HttpResponseMessage firstResponse = await firstResponseTask.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(HttpStatusCode.Conflict, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);
    }

    /// <summary>
    /// Asserts a response is a <c>422 Unprocessable Entity</c> with the FluentValidation-driven
    /// <see cref="ValidationProblemDetails"/> shape this API always returns for validation
    /// failures (see <c>ProblemDetailsFactory.CreateValidation</c>): correct status/title, and an
    /// <c>errors</c> entry for <paramref name="expectedField"/>. Message text isn't asserted
    /// beyond an optional substring check - exact FluentValidation wording is covered by that
    /// validator's own unit tests and isn't worth pinning an HTTP-level test to.
    /// </summary>
    protected static async Task AssertValidationProblemAsync(
        HttpResponseMessage response,
        string expectedField,
        string? expectedMessageSubstring = null)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        ValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.Status);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.True(
            problem.Errors.ContainsKey(expectedField),
            $"Expected an error for field '{expectedField}', got: [{string.Join(", ", problem.Errors.Keys)}]");

        if (expectedMessageSubstring is not null)
        {
            Assert.Contains(
                problem.Errors[expectedField],
                m => m.Contains(expectedMessageSubstring, StringComparison.OrdinalIgnoreCase));
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Factory.ResetDatabaseAsync();
}
