using Ordinis.Domain.Tasks;

namespace Ordinis.UnitTests.Tasks;

/// <summary>
/// Verifies the <see cref="ProjectTaskStatusExtensions"/> state machine — every valid
/// and invalid transition pair, plus terminal state detection.
///
/// The adjacency list under test:
///   Backlog    → ToDo, Cancelled
///   ToDo       → InProgress, Cancelled
///   InProgress → InReview, ToDo, Cancelled
///   InReview   → Done, InProgress, Cancelled
///   Done       → (terminal)
///   Cancelled  → (terminal)
/// </summary>
public sealed class TaskStatusExtensionsTests
{
    #region CanTransitionTo - valid transistions
    public static IEnumerable<object[]> ValidTransitions =>
    [
        [ProjectTaskStatus.Backlog, ProjectTaskStatus.ToDo],
        [ProjectTaskStatus.Backlog, ProjectTaskStatus.Cancelled],
        [ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress],
        [ProjectTaskStatus.ToDo, ProjectTaskStatus.Cancelled],
        [ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview],
        [ProjectTaskStatus.InProgress, ProjectTaskStatus.ToDo],
        [ProjectTaskStatus.InProgress, ProjectTaskStatus.Cancelled],
        [ProjectTaskStatus.InReview, ProjectTaskStatus.Done],
        [ProjectTaskStatus.InReview, ProjectTaskStatus.InProgress],
        [ProjectTaskStatus.InReview, ProjectTaskStatus.Cancelled],
    ];

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void CanTransitionTo_ValidPair_ReturnsTrue(Domain.Tasks.ProjectTaskStatus from, Domain.Tasks.ProjectTaskStatus to)
    {
        var result = from.CanTransitionTo(to);

        Assert.True(result, $"Expected {from} -> {to} to be a valid transition.");
    }
    #endregion

    #region CanTransitionTo - invalid transitions (all remaining 20 pairs)
    public static IEnumerable<object[]> InvalidTransitions =>
    [
        // Self-transitions are always invalid
        [ProjectTaskStatus.Backlog,    ProjectTaskStatus.Backlog],
        [ProjectTaskStatus.ToDo,       ProjectTaskStatus.ToDo],
        [ProjectTaskStatus.InProgress, ProjectTaskStatus.InProgress],
        [ProjectTaskStatus.InReview,   ProjectTaskStatus.InReview],
        [ProjectTaskStatus.Done,       ProjectTaskStatus.Done],
        [ProjectTaskStatus.Cancelled,  ProjectTaskStatus.Cancelled],

        // Backlog cannot skip forward or jump to Done
        [ProjectTaskStatus.Backlog,    ProjectTaskStatus.InProgress],
        [ProjectTaskStatus.Backlog,    ProjectTaskStatus.InReview],
        [ProjectTaskStatus.Backlog,    ProjectTaskStatus.Done],

        // ToDo cannot skip forward, go back, or jump to Done/InReview
        [ProjectTaskStatus.ToDo,       ProjectTaskStatus.Backlog],
        [ProjectTaskStatus.ToDo,       ProjectTaskStatus.InReview],
        [ProjectTaskStatus.ToDo,       ProjectTaskStatus.Done],

        // InProgress cannot go to Backlog or skip to Done
        [ProjectTaskStatus.InProgress, ProjectTaskStatus.Backlog],
        [ProjectTaskStatus.InProgress, ProjectTaskStatus.Done],

        // InReview cannot go to Backlog or ToDo
        [ProjectTaskStatus.InReview,   ProjectTaskStatus.Backlog],
        [ProjectTaskStatus.InReview,   ProjectTaskStatus.ToDo],

        // Terminal states have no outbound transitions
        [ProjectTaskStatus.Done,       ProjectTaskStatus.Backlog],
        [ProjectTaskStatus.Done,       ProjectTaskStatus.ToDo],
        [ProjectTaskStatus.Done,       ProjectTaskStatus.InProgress],
        [ProjectTaskStatus.Done,       ProjectTaskStatus.InReview],
        [ProjectTaskStatus.Done,       ProjectTaskStatus.Cancelled],
        [ProjectTaskStatus.Cancelled,  ProjectTaskStatus.Backlog],
        [ProjectTaskStatus.Cancelled,  ProjectTaskStatus.ToDo],
        [ProjectTaskStatus.Cancelled,  ProjectTaskStatus.InProgress],
        [ProjectTaskStatus.Cancelled,  ProjectTaskStatus.InReview],
        [ProjectTaskStatus.Cancelled,  ProjectTaskStatus.Done],
    ];

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void CanTransitionTo_InvalidPair_ReturnsFalse(ProjectTaskStatus from, ProjectTaskStatus to)
    {
        var result = from.CanTransitionTo(to);

        Assert.False(result, $"Expected {from} -> {to} to be an invalid transition.");
    }
    #endregion

    #region IsTerminal
    [Theory]
    [InlineData(ProjectTaskStatus.Done)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public void IsTerminal_TerminalStatus_ReturnsTrue(ProjectTaskStatus status)
    {
        Assert.True(status.IsTerminal());
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Backlog)]
    [InlineData(ProjectTaskStatus.ToDo)]
    [InlineData(ProjectTaskStatus.InProgress)]
    [InlineData(ProjectTaskStatus.InReview)]
    public void IsTerminal_NonTerminalStatus_ReturnsFalse(ProjectTaskStatus status)
    {
        Assert.False(status.IsTerminal());
    }
    #endregion

    #region GetAllowedTransitions
    [Theory]
    [InlineData(ProjectTaskStatus.Done)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public void GetAllowedTransitions_TerminalStatus_ReturnsEmptySet(ProjectTaskStatus status)
    {
        IReadOnlySet<ProjectTaskStatus> allowed = status.GetAllowedTransitions();

        Assert.Empty(allowed);
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Backlog, new[] { ProjectTaskStatus.ToDo, ProjectTaskStatus.Cancelled })]
    [InlineData(ProjectTaskStatus.ToDo, new[] { ProjectTaskStatus.InProgress, ProjectTaskStatus.Cancelled })]
    [InlineData(ProjectTaskStatus.InReview, new[] { ProjectTaskStatus.Done, ProjectTaskStatus.InProgress, ProjectTaskStatus.Cancelled })]
    public void GetAllowedTransitions_NonTerminalStatus_ReturnsExpectedSet(
        ProjectTaskStatus status,
        ProjectTaskStatus[] expected)
    {
        IReadOnlySet<ProjectTaskStatus> transitions = status.GetAllowedTransitions();

        Assert.Equal(expected.OrderBy(x => x), transitions.OrderBy(x => x));
    }
    #endregion
}
