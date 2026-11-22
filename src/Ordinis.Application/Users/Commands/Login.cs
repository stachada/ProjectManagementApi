using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

// Command
/// <summary>
/// Authenticates a user within a specific organization and issues a new token pair.
/// </summary>
/// <param name="OrganizationSlug">
/// The organization's URL-friendly slug. Required because <c>User.Email</c> is only unique
/// within an organization, not globally - email and password alone can't identify one user.
/// </param>
/// <param name="Email">The user's email address.</param>
/// <param name="Password">The plaintext password supplied by the caller.</param>
/// <returns>A new access/refresh token pair.</returns>
public sealed record Login(
    string OrganizationSlug,
    string Email,
    string Password) : ICommand<LoginResult>;

/// <summary>
/// Represents the result of a successful login operation.
/// </summary>
/// <param name="AccessToken">Short-lived JWT for calling protected endpoints.</param>
/// <param name="RefreshToken">Long-lived opaque token for obtaining new access tokens.</param>
/// <param name="AccessTokenExpiresAt">UTC expiry of <paramref name="AccessToken"/>.</param>
public sealed record LoginResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

// Handler
/// <summary>
/// Handles <see cref="Login"/>
/// </summary>
/// <param name="db">The application's database context.</param>
/// <param name="passwordHasher">Service for hashing and verifying passwords.</param>
/// <param name="jwtTokenService">Service for generating JWT tokens.</param>
/// <param name="timeProvider">Provider for the current time.</param>
internal sealed class LoginHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    TimeProvider timeProvider) : ICommandHandler<Login, LoginResult>
{
    public async Task<LoginResult> HandleAsync(
        Login command,
        CancellationToken cancellationToken)
    {
        // Normalize exactly as the domain does when storing (User.Create / Organization.Create
        // both Trim().ToLowerInvariant()), so matching never depends on the database collation:
        // SQL Server's default collation is case-insensitive, PostgreSQL's is not.
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();
        var normalizedSlug = command.OrganizationSlug.Trim().ToLowerInvariant();

        User? user = await db.Users
            .Where(u => u.Organization!.Slug == normalizedSlug
                && u.Email == normalizedEmail)
            .SingleOrDefaultAsync(cancellationToken);

        // Every failure path below throws the identical exception - see AuthenticationException's
        // doc comment for why reasons are deliberately not distinguished in the response.
        if (user is null || !user.IsActive || !passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            throw new AuthenticationException();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        (string accessToken, DateTimeOffset accessTokenExpiresAt) = jwtTokenService.GenerateAccessToken(user, now);
        (string refreshToken, DateTimeOffset refreshTokenExpiresAt) = jwtTokenService.GenerateRefreshToken(now);

        user.SetRefreshToken(jwtTokenService.HashRefreshToken(refreshToken), refreshTokenExpiresAt);
        await db.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            AccessTokenExpiresAt: accessTokenExpiresAt
        );
    }
}

// Validator
/// <summary>
/// Validates <see cref="Login"/> commands before the handler runs.
/// </summary>
/// <remarks>
/// Deliberately structural only - no existence/credential checks here. Those live in the
/// handler and always fail with the same <see cref="AuthenticationException"/>, so a bad
/// guess at an org slug or email never surfaces as a distinguishable <c>422</c> validation
/// error versus a <c>401</c> auth failure.
/// </remarks>
internal sealed class LoginValidator : AbstractValidator<Login>
{
    public LoginValidator()
    {
        RuleFor(x => x.OrganizationSlug).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
