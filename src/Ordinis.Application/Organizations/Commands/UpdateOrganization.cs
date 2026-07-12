using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Commands;

// Command
/// <summary>
/// Renames an organization and replaces its description in a single unit of work.
/// </summary>
/// <param name="OrganizationId">The organization to update.</param>
/// <param name="NewName">The new display name.</param>
/// <param name="NewDescription">The new description, or <c>null</c> to clear it.</param>
public sealed record UpdateOrganization(
    Guid OrganizationId,
    string NewName,
    string? NewDescription) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UpdateOrganization"/> by loading the organization once, applying both the
/// name and description change, and saving once.
/// </summary>
/// <remarks>
/// <b>Why one command instead of two:</b> this used to be <c>RenameOrganization</c> and
/// <c>UpdateOrganizationDescription</c> sent as two separate <c>SendAsync</c> calls from
/// <c>OrganizationsController.Update</c>, each with its own load/save. That was not atomic - a
/// valid name plus an over-length description committed the rename before the description update
/// failed validation, silently renaming the organization on what looked like a rejected request.
/// Consolidating into one command with one <c>SaveChangesAsync</c> mirrors the fix already applied
/// to <c>ProjectTask.Update</c> for the identical class of bug (see BUILD_PLAN.md Phase 9 Part 4).
/// The old two-command split is removed entirely rather than kept around, since nothing else in
/// <c>src</c> called them individually and leaving them public would reopen the same non-atomic
/// footgun through a second call site.
/// </remarks>
public sealed class UpdateOrganizationHandler(IAppDbContext db) : ICommandHandler<UpdateOrganization>
{
    public async Task HandleAsync(
        UpdateOrganization command,
        CancellationToken cancellationToken = default)
    {
        Organization organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Id == command.OrganizationId, cancellationToken)
                ?? throw new NotFoundException(nameof(Organization), command.OrganizationId);

        organization.Rename(command.NewName);
        organization.UpdateDescription(command.NewDescription);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                nameof(Organization),
                command.OrganizationId,
                ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="UpdateOrganization"/> before the handler runs.
/// </summary>
public sealed class UpdateOrganizationValidator : AbstractValidator<UpdateOrganization>
{
    public UpdateOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty();

        RuleFor(x => x.NewName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.NewDescription)
            .MaximumLength(1000)
            .When(x => x.NewDescription is not null);
    }
}
