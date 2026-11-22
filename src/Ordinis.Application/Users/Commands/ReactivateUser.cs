using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

/// <summary>
/// Reactivates a previously deactivated user account.
/// </summary>
/// <param name="UserId">The ID of the user to reactivate.</param>
/// <param name="RequestedByUserId">
/// The ID of the user performing the action. Only Admins may reactivate
/// accounts — enforced by Phase 8 policy handlers.
/// </param>
/// <param name="IfMatch">
/// The expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// Compared against the current one before the change is applied (stale → 409).
/// </param>
public sealed record ReactivateUser(
    Guid UserId,
    Guid RequestedByUserId,
    byte[]? IfMatch) : ICommand;

/// <summary>
/// Handles <see cref="ReactivateUser"/> commands.
/// </summary>
internal sealed class ReactivateUserHandler(IAppDbContext db)
    : ICommandHandler<ReactivateUser>
{
    public async Task HandleAsync(ReactivateUser command, CancellationToken ct)
    {
        User user = await db.Users
            .SingleOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException(nameof(User), command.UserId);

        ConcurrencyGuard.EnsureMatch(user.RowVersion, command.IfMatch, nameof(User), command.UserId);

        // Domain guards: throws DomainException if already active.
        user.Reactivate();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(nameof(User), command.UserId, ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="ReactivateUser"/> commands before the handler runs.
/// </summary>
internal sealed class ReactivateUserValidator : AbstractValidator<ReactivateUser>
{
    public ReactivateUserValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.RequestedByUserId)
            .NotEmpty();

        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
