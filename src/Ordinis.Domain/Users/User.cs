using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Domain.Users;

/// <summary>
/// Represents a registered user of the system.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregate root.</b> User is the consistency boundary for identity and
/// profile data. Tasks and Comments reference users by <c>UserId</c> - they
/// never navigate back and mutate through the User aggregate.
/// </para>
/// <para>
/// <b>Organization scoping:</b> A user belongs to exactly one
/// <see cref="Organization"/>. Fine-grained project-level
/// permissions are modelled on <c>ProjectMember</c>, not here.
/// </para>
/// <para>
/// <b>Password storage:</b> Only a <see cref="PasswordHash"/> is persisted -
/// never a plaintext password. Hashing is handled at the Application layer
/// before the <c>CreateUser</c> command reaches the domain.
/// </para>
/// </remarks>
public class User : AggregateRoot
{
    #region Identity & profile
    /// <summary>
    /// The user's display name shown across the UI.
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// The user's email address. Used as the login identifier and must be
    /// unique across all users within the same organization.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// BCrypt / Argon2 hash of the user's password.
    /// Never expose this on DTOs or API responses.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// The user's organization-level role. Governs organization-wide
    /// actions (e.g. creating projects, inviting users).
    /// Project-level permissions are on <c>ProjectMember</c>.
    /// </summary>
    public Role OrgRole { get; private set; }

    /// <summary>
    /// Whether this user account is active. Deactivated users cannot
    /// authenticate and are excluded from assignee suggestions.
    /// Prefer deactivation over deletion to preserve audit history.
    /// </summary>
    public bool IsActive { get; private set; }
    #endregion

    #region Foreign keys
    /// <summary>
    /// The ID of the organization this user belongs to.
    /// </summary>
    public Guid OrganizationId { get; private set; }
    #endregion

    #region Navigation properties
    /// <summary>
    /// The organization this user belongs to.
    /// </summary>
    public Organization? Organization { get; private set; }
    #endregion

    #region Refresh token (auth concern kept minimal in the domain)
    /// <summary>
    /// Hashed refresh token. <c>null</c> when no active session exists.
    /// Replaced on every successful refresh; cleared on logout.
    /// </summary>
    public string? RefreshToken { get; private set; }

    /// <summary>
    /// UTC expiry of the current refresh token.
    /// </summary>
    public DateTimeOffset? RefreshTokenExpiresAt { get; private set; }
    #endregion

    #region Constructor
    private User() { }

    /// <summary>
    /// Creates a new active user account.
    /// </summary>
    /// <param name="organizationId">The organization the user belongs to.</param>
    /// <param name="displayName">The user's display name. Must not be empty.</param>
    /// <param name="email">The user's email address. Must not be empty.</param>
    /// <param name="passwordHash">Pre-hashed password from the Application layer.</param>
    /// <param name="orgRole">The user's organization-level role</param>
    /// <exception cref="ArgumentException">
     /// Thrown if <paramref name="displayName"/> or <paramref name="email"/> is empty.
    /// </exception>
    public static User Create(
        Guid organizationId,
        string displayName,
        string email,
        string passwordHash,
        Role orgRole = Role.Member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("OrganizationId cannot be empty." + nameof(organizationId));
        }

        return new User
        {
            OrganizationId = organizationId,
            DisplayName = displayName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            OrgRole = orgRole,
            IsActive = true
        };
    }
    #endregion

    #region Behaviour
    /// <summary>
    /// Updates the user's display name.
    /// </summary>
    /// <param name="newDisplayName">The new display name. Must not be empty.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if the value is empty or whitespace.
    /// </exception>
    public void UpdateDisplayName(string newDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newDisplayName);
        DisplayName = newDisplayName.Trim();
    }

    /// <summary>
    /// Replace the stored password hash.
    /// The Application layer is responsible for hashing before calling this.
    /// </summary>
    /// <param name="newPasswordHash">The new BCrypt/Argon2 hash.</param>
    public void ChangePasswordHash(string newPasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
    }

    /// <summary>
    /// Stores a new refresh token hash and its expiry.
    /// Called by the auth service after issuing a new refresh token.
    /// </summary>
    /// <param name="tokenHash">Hash to the raw refresh token.</param>
    /// <param name="expiresAt">UTC expiry of the refresh token.</param>
    public void SetRefreshToken(string tokenHash, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        RefreshToken = tokenHash;
        RefreshTokenExpiresAt = expiresAt;
    }

    /// <summary>
    /// Clears the stored refresh token. Called on logout or token revocation.
    /// </summary>
    public void RevokeRefreshToken()
    {
        RefreshToken = null;
        RefreshTokenExpiresAt = null;
    }

    /// <summary>
    /// Deactivates the user account. Deactivated users cannot authenticate.
    /// Prefer this over deletion to preserve audit trail integrity.
    /// </summary>
    /// <exception cref="DomainException">
    /// Throw if the user is already inactive.
    /// </exception>
    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new DomainException(
                "User is already inactive.",
                "user.already-inactive");
        }

        IsActive = false;
    }

    /// <summary>
    /// Reactivates a previously deactivated user account.
    /// </summary>
    /// <exception cref="DomainException">
    /// Thrown if the user is already active.
    /// </exception>
    public void Reactivate()
    {
        if (IsActive)
        {
            throw new DomainException(
                "User is already active.",
                "user.already-active");
        }

        IsActive = true;
    }

    /// <summary>
    /// Changes the user's organization-level role.
    /// </summary>
    /// <param name="newRole">The new role to assign.</param>
    public void ChangeOrgRole(Role newRole) => OrgRole = newRole;
    #endregion
}
