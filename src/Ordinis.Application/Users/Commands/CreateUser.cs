using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

// Command
/// <summary>
/// Creates a new user account within an organization.
/// </summary>
/// <param name="OrganizationId">The organization the user will belong to.</param>
/// <param name="DisplayName">The user's display name. 1–100 characters.</param>
/// <param name="Email">
/// The user's email address. Must be unique within the organization.
/// </param>
/// <param name="Password">
/// Plaintext password supplied by the caller. The handler hashes this before
/// passing it to the domain — the domain never receives or stores plaintext.
/// </param>
/// <param name="OrgRole">
/// The user's organization-level role. Defaults to <see cref="Role.Member"/>
/// when omitted.
/// </param>
/// <returns>The <see cref="Guid"/> of the newly created user.</returns>
public sealed record CreateUser(
    Guid OrganizationId,
    string DisplayName,
    string Email,
    string Password,
    Role OrgRole = Role.Member) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="CreateUser"/> commands.
/// </summary>
internal sealed class CreateUserHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher) : ICommandHandler<CreateUser, Guid>
{
    public async Task<Guid> HandleAsync(CreateUser command, CancellationToken cancellationToken)
    {
        // Hash the plaintext password before it reaches the domain.
        // The domain stores only the hash - it never sees the raw value.
        var passwordHash = passwordHasher.Hash(command.Password);

        var user = User.Create(
            organizationId: command.OrganizationId,
            displayName: command.DisplayName,
            email: command.Email,
            passwordHash: passwordHash,
            orgRole: command.OrgRole);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}

// Validator
/// <summary>
/// Validates <see cref="CreateUser"/> commands before the handler runs.
/// </summary>
/// <remarks>
/// Responsibility boundary:
/// <list type="bullet">
///   <item>Structural rules (length, format) — enforced here.</item>
///   <item>
///     Email uniqueness — scoped per organization, matching the domain model
///     and the planned DB unique index on <c>(OrganizationId, Email)</c>.
///   </item>
///   <item>Organization existence — enforced here via async DB check.</item>
///   <item>Password strength rules — enforced here (min length).</item>
///   <item>Role authorization (who may assign Admin) — Phase 8 policy handlers.</item>
/// </list>
/// </remarks>
internal sealed class CreateUserValidator : AbstractValidator<CreateUser>
{
    public CreateUserValidator(IAppDbContext db)
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .MustAsync(async (id, ct) =>
                await db.Organizations
                    .AnyAsync(o => o.Id == id && o.IsActive, ct))
            .WithMessage("Organization does not exist or is suspended.");

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254) // RFC 5321 maximum email length
            .EmailAddress()
            .MustAsync(async (command, email, ct) =>
                !await db.Users.AnyAsync(
                    u => u.OrganizationId == command.OrganizationId
                      && u.Email == email.Trim().ToLowerInvariant(),
                    ct))
            .WithMessage("A user with this email already exists in the organization.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.");

        RuleFor(x => x.OrgRole)
            .IsInEnum();
    }
}
