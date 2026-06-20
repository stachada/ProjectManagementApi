namespace Ordinis.Domain.Users;

/// <summary>
/// Represents a user's permission level within a project.
/// </summary>
/// <remarks>
/// <para>
/// Roles are scoped to a <c>ProjectMember</c> record - the same user can hold
/// different roles in different projects. (Admin in one, Viewer in another).
/// A separate org-level role on <see cref="User"/> governs whether a user can
/// create projects or manage the organization itself.
/// </para>
/// <para>
/// <b>Privilege ordering:</b> Integer values ascend with privilege level,
/// so <c>role &gt;= Role.Member</c> reads as "the user has at least Member access"
/// without requiring a helper method.
/// </para>
/// <para>
/// <b>Semantics:</b>
/// <list type="bullet">
///     <item><see cref="Admin"/> - full control: create/delete boards, manage members, delete tasks.</item>
///     <item><see cref="Member"/> - standard contributor: create/edit/move tasks, add comments.</item>
///     <item><see cref="Viewer"/> - read-only: can view tasks and comments but not modify anything.</item>
/// </list>
/// </para>
/// <para>
/// <b>EF Core mapping:</b> Stored as a <c>varchar</c> string column.
/// Configured in <c>ProjectMemberConfiguration</c> via <c>.HasConversion<string>()</c>.
/// </para>
/// </remarks>
public enum Role
{
    /// <summary>
    /// Read-only access to project content.
    /// </summary>
    Viewer = 0,

    /// <summary>
    /// Standard contributor access; can create and edit tasks.
    /// </summary>
    Member = 1,

    /// <summary>
    /// Full administrative control over the project.
    /// </summary>
    Admin = 2
}
