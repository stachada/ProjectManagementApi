using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Users;

namespace Ordinis.Domain.Projects;

/// <summary>
/// Represents a project within an organization - a container for boards,
/// tasks, and team members working toward a shared goal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregate root.</b> All mutations to a project's membership, boards,
/// and core data go through this class. Boards and <see cref="ProjectMember"/>
/// records are owned by this aggregate and must not be created or removed
/// directly from handlers.
/// </para>
/// <para>
/// <b>Membership and authorization:</b> A user must be a <see cref="ProjectMember"/>
/// to interact with the new project's tasks. The <see cref="Role"/> on the membership
/// determines what operations are permitted. Authorization policies will query
/// <see cref="Members"/> to enforce this.
/// </para>
/// <para>
/// <b>Slug:</b> A URL-friendly unique identifier scoped to the organization
/// (e.g. "backedn-api", "mobile-app"). Immutable after creation to avoid
/// breaking bookmarked URLs and external integrations.
/// </para>
/// <para>
/// <b>Archiving vs. deletion:</b> Archived projects are read-only - no new
/// boards, tasks, or members can be added. Soft-delete
/// (<see cref="AuditableEntity.SoftDelete"/>) is reserved for full removal
/// from normal query results. Prefer archiving over deletion for active projects
/// with existing task history.
/// </para>
/// </remarks>
public class Project : AggregateRoot
{
    #region Private backing fields
    private readonly List<ProjectMember> _members = [];
    private readonly List<Board> _boards = [];
    #endregion

    #region Properties
    /// <summary>
    /// The project's display name (e.g. "Backend API").
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// A URL-friendly unique identifier scoped to the organization
    /// (e.g. "backend-api"). Immutable after creation.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>
    /// An optional description of the project's purpose and scope.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Whether this project is archived. Archived projects are read-only:
    /// no new boards, tasks, or members can be added.
    /// </summary>
    public bool IsArchived { get; private set; }
    #endregion

    #region Foreign keys
    /// <summary>
    /// The ID of the organization this project belongs to.
    /// </summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// The user who created this project. Recorded for audit purposes;
    /// does not imply ongoing ownership or elevated permissions beyond
    /// what the creator's <see cref="ProjectMember"/> role grants.
    /// </summary>
    public Guid CreatedByUserId { get; private set; }
    #endregion

    #region Navigation properties
    /// <summary>
    /// The organization this project belongs to.
    /// </summary>
    public Organization? Organization { get; private set; }

    /// <summary>
    /// The user who created this project.
    /// </summary>
    public User? CreatedByUser { get; private set; }

    /// <summary>
    /// All members of this project. Each entry carries the role the user
    /// holds within this project. Managed via <see cref="AddMember"/>,
    /// <see cref="RemoveMember"/>, and <see cref="ChangeMemberRole"/>.
    /// </summary>
    public IReadOnlyCollection<ProjectMember> Members => _members.AsReadOnly();

    /// <summary>
    /// All boards within this project. Managed via <see cref="AddBoard"/>
    /// and <see cref="ArchiveBoard"/>.
    /// </summary>
    public IReadOnlyCollection<Board> Boards => _boards.AsReadOnly();
    #endregion

    #region Constructors
    private Project() { }

    /// <summary>
    /// Creates a new active project under the given organization, and
    /// automatically adds the creator as an <see cref="Role.Admin"/> member.
    /// </summary>
    /// <param name="organizationId">The owning organization. Must not be empty.</param>
    /// <param name="createdByUserId">The user creating the project. Must not be empty.</param>
    /// <param name="name">Display name. Must not be empty.</param>
    /// <param name="slug">
    /// URL-friendly unique identifier scoped to the organization.
    /// Validated by <c>CreateProjectValidator</c> in the Application layer
    /// before this factory method is called.</param>
    /// <param name="description">Optional description.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="name"/>, <paramref name="slug"/>,
    /// <paramref name="organizationId"/>, or <paramref name="createdByUserId"/>
    /// are empty.
    /// </exception>
    public static Project Create(
        Guid organizationId,
        Guid createdByUserId,
        string name,
        string slug,
        DateTimeOffset occurredAt,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("OrganizationId cannot be an empty Guid.", nameof(organizationId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("CreatedByUserId cannot be an empty Guid.", nameof(createdByUserId));
        }

        var project = new Project
        {
            OrganizationId = organizationId,
            CreatedByUserId = createdByUserId,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            IsArchived = false
        };

        // The creator is automatically and Admin for the project they create.
        project._members.Add(ProjectMember.Create(project.Id, createdByUserId, Role.Admin, occurredAt));

        return project;
    }
    #endregion

    #region Project behaviour
    /// <summary>
    /// Updates the project's display name.
    /// The slug is intentionally not updated - slugs are immutable after
    /// creation to preserve external links and integrations.
    /// </summary>
    /// <param name="newName">New display name. Must not be empty.</param>
    /// <exception cref="DomainException">Thrown if the project is archived.</exception>
    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        EnsureNotArchived();

        Name = newName.Trim();
    }

    /// <summary>
    /// Updates the project's description.
    /// </summary>
    /// <param name="newDescription">New description, or <c>null</c> to clear it.</param>
    /// <exception cref="DomainException">Thrown if the project is archived.</exception>
    public void UpdateDescription(string? newDescription)
    {
        EnsureNotArchived();
        Description = newDescription?.Trim();
    }

    /// <summary>
    /// Archives the project. Archived projects are read-only.
    /// </summary>
    /// <exception cref="DomainException">Thrown if already archived.</exception>
    public void Archive()
    {
        if (IsArchived)
        {
            throw new DomainException(
                "Project is already archived.",
                "project.already-archived"
            );
        }

        IsArchived = true;
    }

    /// <summary>Unarchives the project, making it active again.</summary>
    /// <exception cref="DomainException">Thrown if the project is not archived.</exception>
    public void Unarchive()
    {
        if (!IsArchived)
        {
            throw new DomainException(
                "Project is not archived.",
                "project.not-archived"
            );
        }

        IsArchived = false;
    }
    #endregion

    #region Member management
    /// <summary>
    /// Adds a user to the project with the given role.
    /// </summary>
    /// <param name="userId">The user to add. Must not be empty.</param>
    /// <param name="role">The role to assign within this project.</param>
    /// <exception cref="DomainException">
    /// Thrown if the project is archived, or if the user is already a member.
    /// </exception>
    public void AddMember(Guid userId, Role role, DateTimeOffset occurredAt)
    {
        EnsureNotArchived();

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty", nameof(userId));
        }

        if (_members.Any(m => m.UserId == userId))
        {
            throw new DomainException(
                "User is already a member of the project.",
                "project.member-already-exists"
            );
        }

        var member = ProjectMember.Create(Id, userId, role, occurredAt);
        _members.Add(member);
    }

    /// <summary>
    /// Removes a user from the project.
    /// </summary>
    /// <param name="userId">The user to remove.</param>
    /// <exception cref="DomainException">
    /// Thrown if the project is archived, the user is not a member,
    /// or removing them would leave the project with no Admin.
    /// </exception>
    public void RemoveMember(Guid userId)
    {
        EnsureNotArchived();

        ProjectMember member = _members.FirstOrDefault(m => m.UserId == userId)
            ?? throw new DomainException(
                "User is not a member of the project.",
                "project.member-not-found"
            );

        // Guard: at least one Admin must remain.
        var isLastAdmin = member.Role == Role.Admin
            && _members.Count(m => m.Role == Role.Admin) == 1;

        if (isLastAdmin)
        {
            throw new DomainException(
                "Cannot remove the last Admin from the project. Assign another Admin first.",
                "project.last-admin-removal"
            );
        }

        _members.Remove(member);
    }

    /// <summary>
    /// Changes an existing member's role within the project.
    /// </summary>
    /// <param name="userId">The user whose role should change.</param>
    /// <param name="newRole">The new role to assign.</param>
    /// <exception cref="DomainException">
    /// Thrown if the project is archived, the user is not a member,
    /// or the change would leave the project with no Admin.
    /// </exception>
    public void ChangeMemberRole(Guid userId, Role newRole)
    {
        EnsureNotArchived();

        ProjectMember member = _members.FirstOrDefault(m => m.UserId == userId)
            ?? throw new DomainException(
                "User is not a member of the project.",
                "project.member-not-found"
            );

        // Guard: demoting the last Admin is not permitted.
        var isDemotingLastAdmin = member.Role == Role.Admin
            && newRole != Role.Admin
            && _members.Count(m => m.Role == Role.Admin) == 1;

        if (isDemotingLastAdmin)
        {
            throw new DomainException(
                "Cannot demote the last Admin from the project. Assign another Admin first.",
                "project.last-admin-demotion"
            );
        }

        member.Role = newRole;
    }
    #endregion

    #region Board management
    /// <summary>
    /// Creates a new board within this project.
    /// </summary>
    /// <param name="name">The board's display name. Must not be empty.</param>
    /// <param name="createdByUserId">The user creating the board.</param>
    /// <returns>The newly created <see cref="Board"/>.</returns>
    /// <exception cref="DomainException">
    /// Thrown if the project is archived, or if a board with the same name
    /// already exists within this project.
    /// </exception>
    public Board AddBoard(string name, Guid createdByUserId)
    {
        EnsureNotArchived();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_boards.Any(b => b.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException(
                $"A board named '{name}' already exists in this project.",
                "project.board-name-duplicate"
            );
        }

        var board = Board.Create(Id, name, createdByUserId);
        _boards.Add(board);

        return board;
    }

    /// <summary>
    /// Archives a board within this project.
    /// Archived boards are read-only and hidden from active board listings.
    /// </summary>
    /// <param name="boardId">The Id of the board to archive.</param>
    /// <exception cref="DomainException">
    /// Thrown if the project is archived, or if the board is not found.
    /// </exception>
    public void ArchiveBoard(Guid boardId)
    {
        EnsureNotArchived();

        Board board = _boards.FirstOrDefault(b => b.Id == boardId)
            ?? throw new DomainException(
                "Board not found in this project.",
                "project.board-not-found"
            );

        board.Archive();
    }
    #endregion

    #region Guards
    /// <summary>
    /// Asserts the project is not archived. Called at the start of any
    /// mutation that should be blocked on archived projects.
    /// </summary>
    /// <exception cref="DomainException">Thrown if the project is archived.</exception>
    private void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new DomainException(
                "Operation not permitted: project is archived.",
                "project.archived"
            );
        }
    }
    #endregion
}
