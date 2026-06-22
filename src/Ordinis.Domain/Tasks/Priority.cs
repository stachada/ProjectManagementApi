namespace Ordinis.Domain.Tasks;

/// <summary>
/// Represents the urgency level of a <see cref="Task"/>.
/// </summary>
/// <remarks>
/// <para>
/// Integer values are assigned in ascending order of urgency so that
/// <c>ORDER BY Priority DESC</c> returns the most critical tasks first
/// without any additional mapping or CASE expression in queries.
/// </para>
/// <para>
/// <b>EF Core mapping:</b> Stored as a <c>varchar</c> string column (not an integer)
/// for the same readability and reordering-safety reasons as <see cref="ProjectTaskStatus"/>.
/// Configured in <c>TaskConfiguration</c> via <c>.HasConversion<string>()</c>.
/// </para>
/// </remarks>
public enum Priority
{
    /// <summary>
    /// Nice to have; no time pressure.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Normal work item; should be completed within the current sprint.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Important; should be addressed before lower-priority items.
    /// </summary>
    High = 2,

    /// <summary>
    /// Blocking or production-impacting; requires immediate attention.
    /// </summary>
    Critical = 3
}
