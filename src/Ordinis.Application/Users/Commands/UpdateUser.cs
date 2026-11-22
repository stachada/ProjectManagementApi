using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

/// <summary>
/// Updates a user's display name.
/// </summary>
/// <param name="UserId">The ID of the user to update.</param>
/// <param name="DisplayName">The new display name. 1–100 characters.</param>
/// <param name="RequestedByUserId">
/// The ID of the user performing the update - always the authenticated caller. That only the
/// account owner or an org Admin of the same organization may update a display name is
/// enforced before dispatch by the <c>UserSelfOrOrgAdmin</c> authorization policy.
/// </param>
/// <remarks>
/// Email is immutable after creation (it is the login identifier).
/// There is no password-change operation yet.
/// Role changes go through <c>ChangeUserOrgRole</c>.
/// </remarks>
/// <param name="IfMatch">
/// The expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// Compared against the current one before the change is applied (stale → 409).
/// </param>
public sealed record UpdateUser(
    Guid UserId,
    string DisplayName,
    Guid RequestedByUserId,
    byte[]? IfMatch) : ICommand;

// Validator
/// <summary>
/// Handles <see cref="UpdateUser"/> commands.
/// </summary>
internal sealed class UpdateUserHandler(IAppDbContext db)
    : ICommandHandler<UpdateUser>
{
    public async Task HandleAsync(UpdateUser command, CancellationToken ct)
    {
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException(nameof(User), command.UserId);

        ConcurrencyGuard.EnsureMatch(user.RowVersion, command.IfMatch, nameof(User), command.UserId);

        user.UpdateDisplayName(command.DisplayName);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                nameof(User),
                command.UserId,
                ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="UpdateUser"/> commands before the handler runs.
/// </summary>
internal sealed class UpdateUserValidator : AbstractValidator<UpdateUser>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.RequestedByUserId)
            .NotEmpty();

        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
