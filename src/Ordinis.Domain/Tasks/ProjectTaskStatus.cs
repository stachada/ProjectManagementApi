namespace Ordinis.Domain.Tasks;

/// <summary>
/// Represents the lifecycle status of a <see cref="ProjectTask"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>State machine:</b> Not all transitions between statuses are legal.
/// Use <see cref="ProjectTaskStatusExtensions.CanTransitionTo"/> to validate a
/// proposed move before applying it. Aggregate root methods on <see cref="ProjectTask"/>
/// enforce this - callers never bypass transition validation.
/// </para>
/// <para>
/// <b>Legal transitions:</b>
/// <code>
/// Backlog     -> ToDo, Cancelled
/// ToDo        -> InProgress, Cancelled
/// InProgress  -> InReview, ToDo, Cancelled
/// InReview    -> Done, InProgress, Cancelled
/// Done        -> ToDo (reopen only)
/// Cancelled   -> (terminal - no further transitions)
/// </code>
/// <see cref="ProjectTaskStatusExtensions.IsTerminal"/> is a separate, explicit concept from
/// "has outbound transitions" - it gates <see cref="ProjectTask.Update"/>/<see cref="ProjectTask.Assign"/>/
/// <see cref="ProjectTask.Unassign"/> and still treats <c>Done</c> as terminal even though <c>Move</c>
/// permits the single <c>Done -> ToDo</c> reopen transition. A closed task must be explicitly reopened
/// via <c>Move</c> before it can be edited or reassigned again.
/// </para>
/// <para>
/// <b>EF Core mapping:</b> Stored as a <c>varchar</c> string column (not an integer)
/// so the database values are readable without a lookup table, and reordering
/// the enum numbers never corrupts data. Configured in <c>TaskConfiguration</c>
/// via <c>.HasConversion<string>()</c>.
/// </para>
/// </remarks>
public enum ProjectTaskStatus
{
    /// <summary>
    /// The task exists but has not been scheduled for active work.
    /// Typically lives in the backlog column on a board.
    /// </summary>
    Backlog = 0,

    /// <summary>
    /// The task is scheduled and ready for someone to pick up.
    /// </summary>
    ToDo = 1,

    /// <summary>
    /// Actively being worked on.
    /// </summary>
    InProgress = 2,

    /// <summary>
    /// Work is complete and the task is awaiting review or QA.
    /// </summary>
    InReview = 3,

    /// <summary>
    /// Work has been completed and accepted. Terminal state.
    /// </summary>
    Done = 4,

    /// <summary>
    /// The task has been cancelled.
    /// </summary>
    Cancelled = 5
}

/// <summary>
/// Extension methods for <see cref="ProjectTaskStatus"/> transition validation.
/// </summary>
public static class ProjectTaskStatusExtensions
{
    private static readonly Dictionary<ProjectTaskStatus, IReadOnlySet<ProjectTaskStatus>> AllowedTransitions = new()
    {
        [ProjectTaskStatus.Backlog] = new HashSet<ProjectTaskStatus> { ProjectTaskStatus.ToDo, ProjectTaskStatus.Cancelled },
        [ProjectTaskStatus.ToDo] = new HashSet<ProjectTaskStatus> { ProjectTaskStatus.InProgress, ProjectTaskStatus.Cancelled },
        [ProjectTaskStatus.InProgress] = new HashSet<ProjectTaskStatus> { ProjectTaskStatus.InReview, ProjectTaskStatus.ToDo, ProjectTaskStatus.Cancelled },
        [ProjectTaskStatus.InReview] = new HashSet<ProjectTaskStatus> { ProjectTaskStatus.Done, ProjectTaskStatus.InProgress, ProjectTaskStatus.Cancelled },
        [ProjectTaskStatus.Done] = new HashSet<ProjectTaskStatus> { ProjectTaskStatus.ToDo }, // reopen only
        [ProjectTaskStatus.Cancelled] = new HashSet<ProjectTaskStatus>() // fully terminal
    };

    /// <summary>
    /// Returns whether transitioning from <paramref name="from"/> to
    /// <paramref name="to"/> is a legal move in the task state machine.
    /// </summary>
    /// <param name="from">The task's current status.</param>
    /// <param name="to">The desired status after the transition.</param>
    /// <returns>
    /// <c>true</c> if the transition is allowed; <c>false</c> otherwise.
    /// Always returns <c>false</c> if <paramref name="from"/> equals
    /// <paramref name="to"/> - transitioning to the same state is a no-op
    /// and should be rejected before calling this method.
    /// </returns>
    public static bool CanTransitionTo(this ProjectTaskStatus from, ProjectTaskStatus to)
        => from != to
            && AllowedTransitions[from].Contains(to);

    /// <summary>
    /// Returns all statuses that can be legally reached from <paramref name="from"/>.
    /// Returns an empty collection for <see cref="ProjectTaskStatus.Cancelled"/> (fully terminal)
    /// and a single-element set (<see cref="ProjectTaskStatus.ToDo"/>) for
    /// <see cref="ProjectTaskStatus.Done"/> (reopen only).
    /// </summary>
    /// <param name="from">The task's current status.</param>
    public static IReadOnlySet<ProjectTaskStatus> GetAllowedTransitions(this ProjectTaskStatus from)
        => AllowedTransitions[from];

    /// <summary>
    /// Returns whether <paramref name="status"/> is a terminal business state -
    /// <see cref="ProjectTaskStatus.Done"/> or <see cref="ProjectTaskStatus.Cancelled"/>.
    /// </summary>
    /// <remarks>
    /// This is deliberately independent of <see cref="GetAllowedTransitions"/>: <c>Done</c> still
    /// permits the single <c>Done -> ToDo</c> reopen move via <see cref="ProjectTask.Move"/>, but
    /// remains terminal for every other mutation (<see cref="ProjectTask.Update"/>,
    /// <see cref="ProjectTask.Assign"/>, <see cref="ProjectTask.Unassign"/>) until it is reopened.
    /// </remarks>
    /// <param name="status">The status to evaluate.</param>
    public static bool IsTerminal(this ProjectTaskStatus status)
        => status is ProjectTaskStatus.Done or ProjectTaskStatus.Cancelled;
}
