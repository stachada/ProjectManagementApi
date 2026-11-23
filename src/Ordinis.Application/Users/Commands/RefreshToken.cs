using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

// Command
/// <summary>
/// Exchanges a valid refresh token for a new access/refresh token pair.
/// </summary>
/// <param name="Token">The raw (unhashed) refresh token presented by the caller.</param>
/// <returns>A new access/refresh token pair. The presented refresh token is invalidated.</returns>
public sealed record RefreshToken(string Token) : ICommand<LoginResult>;

// Handler
/// <summary>
/// Handles <see cref="RefreshToken"/> commands.
/// </summary>
/// <remarks>
/// Rotation: every successful refresh issues a brand-new refresh token and overwrites the
/// stored hash, so a presented token can only ever be used once - replaying an intercepted
/// refresh token after the legitimate client has already rotated past it fails lookup here
/// (the hash on the user no longer matches), surfacing as <see cref="AuthenticationException"/>.
/// </remarks>
internal sealed class RefreshTokenHandler(
    IAppDbContext db,
    IJwtTokenService jwtTokenService,
    TimeProvider timeProvider) : ICommandHandler<RefreshToken, LoginResult>
{
    public async Task<LoginResult> HandleAsync(
        RefreshToken command,
        CancellationToken cancellationToken)
    {
        var presentedHash = jwtTokenService.HashRefreshToken(command.Token);
        DateTimeOffset now = timeProvider.GetUtcNow();

        User? user = await db.Users
            .SingleOrDefaultAsync(u => u.RefreshToken == presentedHash, cancellationToken);

        if (user is null || user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt <= now || !user.IsActive)
        {
            throw new AuthenticationException("Invalid or expired refresh token.");
        }

        (string accessToken, DateTimeOffset accessTokenExpiresAt) = jwtTokenService.GenerateAccessToken(user, now);
        (string refreshToken, DateTimeOffset refreshTokenExpiresAt) = jwtTokenService.GenerateRefreshToken(now);

        user.SetRefreshToken(jwtTokenService.HashRefreshToken(refreshToken), refreshTokenExpiresAt);
        await db.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            AccessTokenExpiresAt: accessTokenExpiresAt);
    }
}

// Validator
/// <summary>
/// Validates <see cref="RefreshToken"/> commands before the handler runs.
/// </summary>
internal sealed class RefreshTokenValidator : AbstractValidator<RefreshToken>
{
    public RefreshTokenValidator()
    {
        // Named after the request field (POST /auth/refresh sends "refreshToken"), so a client can
        // match the error to what it sent; the command cannot use that name itself, because a
        // record member may not share its enclosing type's name.
        RuleFor(x => x.Token).NotEmpty().OverridePropertyName("RefreshToken");
    }
}
