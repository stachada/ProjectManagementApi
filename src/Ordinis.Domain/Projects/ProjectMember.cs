using Ordinis.Domain.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Domain.Projects;

/// <summary>
/// Represents a user's membership in a project and role within a specific project.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not an aggregate root.</b> <c>ProjectMember</c> is owned by the
/// <see cref="Project"/> aggregate. Create and remove membership through
/// <see cref="Project.AddMember"/> and <see cref="Project.RemoveMember"/> -
/// never instantiate or delete this entity directly from a handler.
/// </para>
/// <para>
/// <b>Role scoping:</b> The same user can hold different <see cref="Role"/>
/// values in different projects (Admin in Project A, Viewer in Project B).
/// This is the primary authorization boundary for all task operations.
/// </para>
/// <para>
/// <b>Uniqueness:</b> The combination of (<see cref="ProjectId"/>, <see cref="UserId"/>)
/// is unique - a user can only have one role per project. Enforced via a
/// unique index in <c>ProjectMemberConfiguration</c>.
/// </para>
/// </remarks>
public class ProjectMember : Entity
{
    /// <summary>
    /// The project this membership belongs to.
    /// </summary>
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// The user who is a member of the project.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// The user's role within this project.
    /// Can be updated via <see cref="Project.ChangeMemberRole"/>.
    /// </summary>
    public Role Role { get; internal set; }

    /// <summary>
    /// UTC timestamp of when the user was added to the project.
    /// </summary>
    public DateTimeOffset JoinedAt { get; private set; }

    #region Navigation properties
    /// <summary>
    /// The project this membership belongs to.
    /// </summary>
    public Project? Project { get; private set; }

    /// <summary>
    /// The user who holds this membership.
    /// </summary>
    public User? User { get; private set; }
    #endregion

    private ProjectMember() { }

    /// <summary>
    /// Creates a new project membership.
    /// Called exclusively from <see cref="Project.AddMember"/>.
    /// </summary>
    internal static ProjectMember Create(Guid projectId, Guid userId, Role role, DateTimeOffset joinedAt)
        => new ()
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            JoinedAt = joinedAt
        };
}
