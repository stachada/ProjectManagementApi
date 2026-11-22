using Microsoft.EntityFrameworkCore;

namespace Ordinis.Application.Common;

/// <summary>
/// The one definition of a name clash for projects (unique per organization) and organizations
/// (globally unique), shared by the create and rename validators.
/// </summary>
/// <remarks>
/// <para>
/// Slugs are generated from the name at creation and never change afterwards, so a rename only
/// changes the display name. A candidate name therefore clashes with another resource if either
/// (a) its slug equals that resource's slug — the creation-time rule, backed by a unique index —
/// or (b) it equals that resource's <i>current</i> display name, ignoring case, which may differ
/// from its slug after a rename.
/// </para>
/// <para>
/// Soft-deleted projects count: their slugs stay reserved (the unique index covers them, and
/// slugs are permanent identifiers), so their names are never reused.
/// </para>
/// </remarks>
public static class NameUniquenessQueries
{
    /// <summary>
    /// Whether <paramref name="name"/> clashes with another project of the organization.
    /// </summary>
    /// <param name="excludeProjectId">The project being renamed, or <see langword="null"/> on creation.</param>
    public static Task<bool> IsProjectNameTakenAsync(
        IAppDbContext db, ISlugGenerator slugGenerator, Guid organizationId, string name, Guid? excludeProjectId,
        CancellationToken cancellationToken)
    {
        string slug = slugGenerator.Slugify(name);
        string lowerName = name.Trim().ToLowerInvariant();

        return db.Projects
            .IgnoreQueryFilters()
            .AnyAsync(p => p.OrganizationId == organizationId
                && p.Id != excludeProjectId
                && (p.Slug == slug || p.Name.ToLower() == lowerName),
                cancellationToken);
    }

    /// <summary>Whether <paramref name="name"/> clashes with another organization.</summary>
    /// <param name="excludeOrganizationId">The organization being renamed, or <see langword="null"/> on creation.</param>
    public static Task<bool> IsOrganizationNameTakenAsync(
        IAppDbContext db, ISlugGenerator slugGenerator, string name, Guid? excludeOrganizationId,
        CancellationToken cancellationToken)
    {
        string slug = slugGenerator.Slugify(name);
        string lowerName = name.Trim().ToLowerInvariant();

        return db.Organizations.AnyAsync(o => o.Id != excludeOrganizationId
            && (o.Slug == slug || o.Name.ToLower() == lowerName),
            cancellationToken);
    }
}
