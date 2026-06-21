using Ordinis.Domain.Common;

namespace Ordinis.Domain.Organizations;

/// <summary>
/// Represents a tenant organization - the top-level boundary that contains
/// projects, boards, tasks, and users.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregate root.</b> All mutations to an organization's core data
/// (name, status, slug) go through this class. Projects are separate
/// aggregate roots - they are not owned by Organization in the DDD sense,
/// but they reference it via <c>OrganizationId</c>.
/// </para>
/// <para>
/// <b>Slug:</b> A URL-friendly unique identifier derived from the organization
/// name (e.g. "Acme Corp" -> "acme-corp"). Used in API routes and visible
/// to users. Immutable after creation to avoid breaking bookmarked URLs;
/// expose a dedicated rename operation if slug changes are ever needed.
/// </para>
/// <para>
/// <b>Suspension vs. deletion:</b> Suspended organizations remain in the
/// database with all their data intact. No new projects or users can be
/// added while suspended. Soft-delete (<see cref="AuditableEntity.SoftDelete"/>)
/// is reserved for full removal from normal query results.
/// </para>
/// </remarks>
public class Organization : AggregateRoot
{
    #region Properties
    /// <summary>
    /// The organization's display name (e.g. "Acme Corp").
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// URL-friendly unique slug derived from the name (e.g. "acme-corp").
    /// Immutable after creation. Used in API route prefixes and external links.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>
    /// Optional description of the organization's purpose.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Whether the organization is currently active.
    /// Suspended organizations reject new projects, boards, and user invitations.
    /// </summary>
    public bool IsActive { get; private set; }
    #endregion

    #region Constructors
    private Organization() { }

    /// <summary>
    /// Creates a new active organization.
    /// </summary>
    /// <param name="name">Display name. Must not be empty.</param>
    /// <param name="slug">
    /// URL-friendly unique identifier. Must be lowercase, alphanumeric with hyphens.
    /// Validated by the Application layer's <c>CreateOrganizationValidator</c>
    /// before reaching this factory method.
    /// </param>
    /// <param name="description">Optional description</param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="name"/> or <paramref name="slug"/> is empty.
    /// </exception>
    public static Organization Create(
        string name,
        string slug,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return new Organization
        {
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            IsActive = true
        };
    }
    #endregion

    #region Behaviour
    /// <summary>
    /// Updates the organization's display name.
    /// The slug is intentionally not updated - slugs are immutable after creation.
    /// </summary>
    /// <param name="newName">The new display name. Must not be empty.</param>
    /// <exception cref="DomainException">Thrown if the organization is suspended.</exception>
    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        EnsureActive();

        Name = newName.Trim();
    }

    /// <summary>
    /// Updates the organization's description.
    /// </summary>
    /// <param name="newDescription">New description, or <c>null</c> to clear it.</param>
    /// <exception cref="DomainException">Thrown if the organization is suspended.</exception>
    public void UpdateDescription(string? newDescription)
    {
        EnsureActive();
        Description = newDescription?.Trim();
    }

    /// <summary>
    /// Suspends the organization. While suspended, no new projects or users
    /// can be added, and existing users cannot authenticate under this org.
    /// </summary>
    /// <exception cref="DomainException">Thrown if already suspended.</exception>
    public void Suspend()
    {
        if (!IsActive)
        {
            throw new DomainException(
                "Organization is already suspended.",
                "organization.already-suspended"
            );
        }

        IsActive = false;
    }

    /// <summary>Reactivates a suspended organization.</summary>
    /// <exception cref="DomainException">Thrown if already active.</exception>
    public void Reactivate()
    {
        if (IsActive)
        {
            throw new DomainException(
                "Organization is already active.",
                "organization.already-active"
            );
        }

        IsActive = true;
    }
    #endregion

    #region Guards
    /// <summary>
    /// Asserts the organization is active. Called at the start of any
    /// mutation that should be blocked while suspended.
    /// </summary>
    /// <exception cref="DomainException">Thrown if the organization is not active.</exception>
    internal void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainException(
                "Operation not permitted: organization is suspended.",
                "organization.suspended"
            );
        }
    }
    #endregion
}
