using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

// Command
/// <summary>
/// Revokes the caller's current refresh token, ending their session.
/// </summary>
/// <param name="UserId">
/// The authenticated user's ID, taken from the access token's <c>sub</c> claim by the Minimal
/// API endpoint - never supplied directly by the caller in the request body.
/// </param>
/// <remarks>
/// Does not revoke the still-live access token - access tokens are short-lived (15 min) and
/// stateless/unrevocable by design, matching the JWT model this API uses; logout only
/// prevents the session from being *extended* past the current access token's expiry.
/// </remarks>
public sealed record Logout(Guid UserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="Logout"/> commands.
/// </summary>
internal sealed class LogoutHandler(IAppDbContext db) : ICommandHandler<Logout>
{
    public async Task HandleAsync(Logout command, CancellationToken cancellationToken)
    {
        User? user = await db.Users
            .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        // Idempotent by design: logging out twice, or logging out with no active session,
        // is not an error - RevokeRefreshToken() on an already-null token is a no-op, and a
        // missing user (deleted between token issuance and logout) has nothing left to revoke.
        user?.RevokeRefreshToken();
        await db.SaveChangesAsync(cancellationToken);
    }
}

// Validator
/// <summary>
/// Validates <see cref="Logout"/> commands before the handler runs.
/// </summary>
internal sealed class LogoutValidator : AbstractValidator<Logout>
{
    public LogoutValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
