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
/// The ID of the user performing the update. Used for authorization
/// in Phase 8 — only the account owner or an Admin may update a display name.
/// </param>
/// <remarks>
/// Email is immutable after creation (it is the login identifier).
/// Password changes go through a dedicated command in Phase 8.
/// Role changes go through <c>ChangeUserOrgRole</c>.
/// </remarks>
public sealed record UpdateUser(
    Guid UserId,
    string DisplayName,
    Guid RequestedByUserId) : ICommand;

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
    }
}
