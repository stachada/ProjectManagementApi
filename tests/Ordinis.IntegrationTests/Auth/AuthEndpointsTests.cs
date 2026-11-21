using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Api.MinimalApis;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Users;
using Ordinis.Infrastructure.Persistence;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Auth;

public sealed class AuthEndpointsTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Login_ValidCredentials_Returns200WithAccessAndRefreshTokens()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, _) = await SeedUserWithPasswordAsync(password);

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug, email, password));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(content);
        Assert.False(string.IsNullOrEmpty(content!.AccessToken));
        Assert.False(string.IsNullOrEmpty(content.RefreshToken));
    }

    [Fact]
    public async Task Login_OrganizationSlugInDifferentCase_Returns200()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, _) = await SeedUserWithPasswordAsync(password);

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug.ToUpperInvariant(), email, password));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(content);
        Assert.False(string.IsNullOrEmpty(content!.AccessToken));
        Assert.False(string.IsNullOrEmpty(content.RefreshToken));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithGenericMessage()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, _) = await SeedUserWithPasswordAsync(password);

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug.ToUpperInvariant(), email, "WrongPassword123!"));
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("Invalid organization, email, or password.", content!.Detail);
    }

    [Fact]
    public async Task Login_UnknownOrganizationSlug_Returns401WithSameMessageAsWrongPassword()
    {
        // Arrange
        const string password = "Password123!";
        var (_, email, _) = await SeedUserWithPasswordAsync(password);

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest("WrongSlug", email, password));
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("Invalid organization, email, or password.", content!.Detail);
    }

    [Fact]
    public async Task Login_DeactivatedUser_Returns401()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, _) = await SeedUserWithPasswordAsync(password, deactivated: true);

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug, email, password));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("Invalid organization, email, or password.", content!.Detail);
    }

    [Fact]
    public async Task Login_EmptyFields_Returns422()
    {
        // Arrange
        const string password = "Password123!";
        var (_, _, _) = await SeedUserWithPasswordAsync(password, deactivated: true);

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest("", "", ""));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
    }

    [Fact]
    public async Task Login_IssuedAccessToken_IsAcceptedByProtectedEndpoint()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, userId) = await SeedUserWithPasswordAsync(password);

        HttpResponseMessage loginResponse = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug, email, password));
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(loginContent);
        Assert.False(string.IsNullOrEmpty(loginContent!.AccessToken));

        // Act
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginContent.AccessToken);
        HttpResponseMessage protectedResponse = await Client.GetAsync($"/api/v1/users/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewTokenPair()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, _) = await SeedUserWithPasswordAsync(password);

        HttpResponseMessage loginResponse = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug, email, password));
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(loginContent);
        Assert.False(string.IsNullOrEmpty(loginContent!.RefreshToken));

        // Act
        HttpResponseMessage refreshResponse = await Client.PostAsJsonAsync($"/auth/refresh", new RefreshTokenRequest(loginContent.RefreshToken));

        // Assert
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshContent = await refreshResponse.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(refreshContent);
        Assert.False(string.IsNullOrEmpty(refreshContent!.AccessToken));
        Assert.False(string.IsNullOrEmpty(refreshContent.RefreshToken));
        Assert.NotEqual(loginContent.AccessToken, refreshContent.AccessToken);
        Assert.NotEqual(loginContent.RefreshToken, refreshContent.RefreshToken);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_Return401()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, _) = await SeedUserWithPasswordAsync(password);

        HttpResponseMessage loginResponse = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug, email, password));
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(loginContent);
        Assert.False(string.IsNullOrEmpty(loginContent!.RefreshToken));

        HttpResponseMessage refreshResponse1 = await Client.PostAsJsonAsync($"/auth/refresh", new RefreshTokenRequest(loginContent.RefreshToken));
        var refreshContent1 = await refreshResponse1.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(refreshContent1);
        Assert.False(string.IsNullOrEmpty(refreshContent1!.RefreshToken));

        // Act
        HttpResponseMessage refreshResponse2 = await Client.PostAsJsonAsync($"/auth/refresh", new RefreshTokenRequest(loginContent.RefreshToken));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse2.StatusCode);
        var refreshContent2 = await refreshResponse2.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(refreshContent2);
        Assert.Equal("Invalid or expired refresh token.", refreshContent2!.Detail);
    }

    [Fact]
    public async Task Logout_ThenRefreshWithPerviousToken_Returns401()
    {
        // Arrange
        const string password = "Password123!";
        var (organizationSlug, email, userId) = await SeedUserWithPasswordAsync(password);

        HttpResponseMessage loginResponse = await Client.PostAsJsonAsync($"/auth/login", new LoginRequest(organizationSlug, email, password));
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(loginContent);
        Assert.False(string.IsNullOrEmpty(loginContent!.AccessToken));
        Assert.False(string.IsNullOrEmpty(loginContent.RefreshToken));

        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginContent.AccessToken);

        HttpResponseMessage logoutResponse = await Client.PostAsync($"/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // Act
        HttpResponseMessage refreshResponse = await Client.PostAsJsonAsync($"/auth/refresh", new RefreshTokenRequest(loginContent.RefreshToken));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
        var refreshContent = await refreshResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(refreshContent);
        Assert.Equal("Invalid or expired refresh token.", refreshContent!.Detail);
    }

    [Fact]
    public async Task Logout_WithoutAccessToken_Returns401()
    {
        // Act
        HttpResponseMessage logoutResponse = await Client.PostAsync($"/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, logoutResponse.StatusCode);
    }

    private async Task<(string OrganizationSlug, string Email, Guid UserId)> SeedUserWithPasswordAsync(
        string password,
        Role orgRole = Role.Member,
        bool deactivated = false)
    {
        using IServiceScope scope = CreateScope();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var organization = Organization.Create(
            name: $"Organization-{Guid.CreateVersion7()}",
            slug: $"organization-{Guid.CreateVersion7()}",
            description: "Test organization");

        var user = User.Create(
            organizationId: organization.Id,
            displayName: "John Doe",
            email: $"user-{Guid.NewGuid()}@example.com",
            passwordHash: passwordHasher.Hash(password),
            orgRole: orgRole);

        if (deactivated)
        {
            user.Deactivate();
        }

        db.Organizations.Add(organization);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        //await AuthenticateAsAsync(user.Id);

        return (organization.Slug, user.Email, user.Id);
    }
}
