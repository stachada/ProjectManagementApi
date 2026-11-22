using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.UnitTests.Application.Common;

/// <summary>
/// Verifies <see cref="EntityQueryableExtensions.OrderByEnumValue{T, TEnum}"/> ranks by the enum's
/// declared numeric order rather than by name. Runs LINQ-to-objects over the same expression tree
/// EF Core translates; the SQL translation itself (through the string value converter) is
/// exercised against SQL Server by <c>Ordinis.AcceptanceTests</c> (task-listing.feature).
/// </summary>
public class EntityQueryableExtensionsTests
{
    private sealed record Row(Guid Id, Priority Priority, ProjectTaskStatus Status);

    private static readonly Row[] Rows =
    [
        new(Guid.CreateVersion7(), Priority.Medium, ProjectTaskStatus.ToDo),
        new(Guid.CreateVersion7(), Priority.Critical, ProjectTaskStatus.InProgress),
        new(Guid.CreateVersion7(), Priority.Low, ProjectTaskStatus.Backlog),
        new(Guid.CreateVersion7(), Priority.High, ProjectTaskStatus.InReview),
    ];

    [Fact]
    public void OrderByEnumValue_Ascending_SortsBySeverityNotAlphabetically()
    {
        Priority[] sorted = Rows.AsQueryable().OrderByEnumValue(r => r.Priority).Select(r => r.Priority).ToArray();

        Assert.Equal([Priority.Low, Priority.Medium, Priority.High, Priority.Critical], sorted);
    }

    [Fact]
    public void OrderByEnumValue_Descending_PutsMostSevereFirst()
    {
        Priority[] sorted = Rows.AsQueryable().OrderByEnumValue(r => r.Priority, descending: true).Select(r => r.Priority).ToArray();

        Assert.Equal([Priority.Critical, Priority.High, Priority.Medium, Priority.Low], sorted);
    }

    [Fact]
    public void OrderByEnumValue_Status_SortsByWorkflowPosition()
    {
        ProjectTaskStatus[] sorted = Rows.AsQueryable().OrderByEnumValue(r => r.Status).Select(r => r.Status).ToArray();

        // Alphabetical would be Backlog, InProgress, InReview, ToDo.
        Assert.Equal(
            [ProjectTaskStatus.Backlog, ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview],
            sorted);
    }

    [Theory]
    [InlineData(Priority.Low, 0)]
    [InlineData(Priority.Medium, 1)]
    [InlineData(Priority.High, 2)]
    [InlineData(Priority.Critical, 3)]
    public void EnumRank_MapsEachValueToItsDeclaredPosition(Priority priority, int expectedRank)
    {
        Func<Row, int> rank = EntityQueryableExtensions.EnumRank<Row, Priority>(r => r.Priority).Compile();

        Assert.Equal(expectedRank, rank(new Row(Guid.CreateVersion7(), priority, ProjectTaskStatus.Backlog)));
    }

    [Fact]
    public void EnumRank_UndefinedValue_SortsLast()
    {
        Func<Row, int> rank = EntityQueryableExtensions.EnumRank<Row, Priority>(r => r.Priority).Compile();

        Assert.Equal(Enum.GetValues<Priority>().Length, rank(new Row(Guid.CreateVersion7(), (Priority)99, ProjectTaskStatus.Backlog)));
    }
}
