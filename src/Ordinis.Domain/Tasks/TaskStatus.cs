namespace Ordinis.Domain.Tasks;

/// <summary>
/// Represents the lifecycle status of a <see cref="ProjectTask"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>State machine:</b> Not all transitions between statuses are legal.
/// Use <see cref="TaskStatusExtensions.CanTransitionTo"/> to validate a
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
/// Done        -> (terminal - no further transitions)
/// Cancelled   -> (terminal - no further transitions)
/// </code>
/// </para>
/// <para>
/// <b>EF Core mapping:</b> Stored as a <c>varchar</c> string column (not an integer)
/// so the database values are readable without a lookup table, and reordering
/// the enum numbers never corrupts data. Configured in <c>TaskConfiguration</c>
/// via <c>.HasConversion<string>()</c>.
/// </para>
/// </remarks>
public enum TaskStatus
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
/// Extension methods for <see cref="TaskStatus"/> transition validation.
/// </summary>
public static class TaskStatusExtensions
{
    private static readonly Dictionary<TaskStatus, IReadOnlySet<TaskStatus>> AllowedTransitions = new()
    {
        [TaskStatus.Backlog] = new HashSet<TaskStatus> { TaskStatus.ToDo, TaskStatus.Cancelled },
        [TaskStatus.ToDo] = new HashSet<TaskStatus> { TaskStatus.InProgress, TaskStatus.Cancelled },
        [TaskStatus.InProgress] = new HashSet<TaskStatus> { TaskStatus.InReview, TaskStatus.ToDo, TaskStatus.Cancelled },
        [TaskStatus.InReview] = new HashSet<TaskStatus> { TaskStatus.Done, TaskStatus.InProgress, TaskStatus.Cancelled },
        [TaskStatus.Done] = new HashSet<TaskStatus>(), // terminal
        [TaskStatus.Cancelled] = new HashSet<TaskStatus>() // terminal
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
    public static bool CanTransitionTo(this TaskStatus from, TaskStatus to)
        => from != to
            && AllowedTransitions[from].Contains(to);

    /// <summary>
    /// Returns all statuses that can be legally reached from <paramref name="from"/>.
    /// Returns an empty collection for terminal states (<see cref="TaskStatus.Done"/>
    /// and <see cref="TaskStatus.Cancelled"/>).
    /// </summary>
    /// <param name="from">The task's current status.</param>
    public static IReadOnlySet<TaskStatus> GetAllowedTransitions(this TaskStatus from)
        => AllowedTransitions[from];

    /// <summary>
    /// Returns whether <paramref name="status"/> is a terminal state from which
    /// no further transitions are allowed.
    /// </summary>
    /// <param name="status">The status to evaluate.</param>
    public static bool IsTerminal(this TaskStatus status)
        => AllowedTransitions[status].Count == 0;
}
