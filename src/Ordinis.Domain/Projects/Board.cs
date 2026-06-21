using Ordinis.Domain.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Domain.Projects;

/// <summary>
/// Represents a board within a project — the visual workspace where tasks
/// are organised into columns and tracked through their lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregate root.</b> Board guards its own invariants: tasks cannot be
/// added to an archived board, and the board's name must be unique within
/// its project (enforced by <see cref="Project.AddBoard"/>).
/// </para>
/// <para>
/// <b>Ownership:</b> Board is created and archived exclusively through the
/// <see cref="Project"/> aggregate (<see cref="Project.AddBoard"/> and
/// <see cref="Project.ArchiveBoard"/>). Never instantiate or mutate a board
/// directly from a command handler — always go through the Project.
/// </para>
/// <para>
/// <b>Tasks:</b> Tasks are owned by the Board aggregate. A task is created
/// on a specific board via <c>Board.CreateTask(...)</c> (implemented in the
/// Task entity phase). Moving a task between boards is a deliberate operation
/// modelled as a state transition on the Task aggregate.
/// </para>
/// <para>
/// <b>Archiving vs. deletion:</b> Archived boards are read-only and excluded
/// from active board listings, but their tasks remain accessible for audit
/// and reporting purposes. Soft-delete (<see cref="AuditableEntity.SoftDelete"/>)
/// is reserved for full removal from query results.
/// </para>
/// </remarks>
public sealed class Board : AggregateRoot
{
    #region Properties
    /// <summary>
    /// The board's display name (e.g. "Sprint Board", "Backlog", "Bug Tracker").
    /// Must be unique within the owning project — enforced by
    /// <see cref="Project.AddBoard"/>.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional description of the board's purpose.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Whether this board is archived. Archived boards are read-only:
    /// no new tasks can be added and existing tasks cannot be moved.
    /// </summary>
    public bool IsArchived { get; private set; }
    #endregion

    #region Foreign keys
    /// <summary>The project this board belongs to.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// The user who created this board. Recorded for audit purposes.
    /// </summary>
    public Guid CreatedByUserId { get; private set; }
    #endregion

    #region Navigation properties
    /// <summary>The project this board belongs to.</summary>
    public Project? Project { get; private set; }

    /// <summary>The user who created this board.</summary>
    public User? CreatedByUser { get; private set; }
    #endregion

    #region Constructor
    private Board() { }

    /// <summary>
    /// Creates a new active board within a project.
    /// Called exclusively from <see cref="Project.AddBoard"/> — never directly.
    /// </summary>
    /// <param name="projectId">The owning project. Must not be empty.</param>
    /// <param name="name">Display name. Must not be empty.</param>
    /// <param name="createdByUserId">The user creating the board.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="name"/> is empty or
    /// <paramref name="projectId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    internal static Board Create(
        Guid projectId,
        string name,
        Guid createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("ProjectId cannot be empty.", nameof(projectId));
        }

        return new Board
        {
            ProjectId = projectId,
            CreatedByUserId = createdByUserId,
            Name = name.Trim(),
            IsArchived = false
        };
    }
    #endregion

    #region Behaviour
    /// <summary>
    /// Updates the board's display name.
    /// </summary>
    /// <param name="newName">New display name. Must not be empty.</param>
    /// <exception cref="DomainException">Thrown if the board is archived.</exception>
    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        EnsureNotArchived();

        Name = newName.Trim();
    }

    /// <summary>Updates the board's description.</summary>
    /// <param name="description">New description, or <c>null</c> to clear it.</param>
    /// <exception cref="DomainException">Thrown if the board is archived.</exception>
    public void UpdateDescription(string? newDescription)
    {
        EnsureNotArchived();
        Description = newDescription?.Trim();
    }

    /// <summary>
    /// Archives this board. Called exclusively from
    /// <see cref="Project.ArchiveBoard"/> — never directly from a handler.
    /// </summary>
    /// <exception cref="DomainException">Thrown if already archived.</exception>
    internal void Archive()
    {
        if (IsArchived)
        {
            throw new DomainException(
                "Board is already archived.",
                "board.already-archived");
        }

        IsArchived = true;
    }

    /// <summary>Unarchives this board, making it active again.</summary>
    /// <exception cref="DomainException">Thrown if the board is not archived.</exception>
    public void Unarchive()
    {
        if (!IsArchived)
        {
            throw new DomainException(
                "Board is not archived.",
                "board.not-archived");
        }

        IsArchived = false;
    }
    #endregion

    #region Guards
    /// <summary>
    /// Asserts the board is not archived. Called at the start of any
    /// mutation that should be blocked on archived boards.
    /// </summary>
    /// <exception cref="DomainException">Thrown if the board is archived.</exception>
    internal void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new DomainException(
                "Operation not permitted: board is archived.",
                "board.archived");
        }
    }
    #endregion
}
