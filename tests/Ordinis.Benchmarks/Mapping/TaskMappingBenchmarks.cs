using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Mapster;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Tasks;

namespace Ordinis.Benchmarks.Mapping;

/// <summary>
/// <see cref="TaskMapper.ToDto"/> (hand-written) versus an equivalent Mapster configuration,
/// mapping 1, 100 and 10&#160;000 fully populated tasks.
/// </summary>
/// <remarks>
/// Each task carries an assignee, three comments, two attachments and a concurrency token, so
/// every rule in the mapping runs - including nested collections and dictionary lookups, which
/// is where convention mappers usually pay for their generality. <see cref="GlobalSetup"/>
/// asserts both mappers produce identical JSON before anything is measured.
/// </remarks>
[MemoryDiagnoser]
public class TaskMappingBenchmarks
{
    private List<ProjectTask> _tasks = null!;
    private Dictionary<Guid, string> _userLookup = null!;
    private TypeAdapterConfig _mapsterConfig = null!;

    /// <summary>How many tasks are mapped per operation.</summary>
    [Params(1, 100, 10_000)]
    public int TaskCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Fixed instant: mapping cost doesn't depend on the clock, and a constant keeps runs reproducible.
        var now = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        Guid[] userIds = [.. Enumerable.Range(0, 20).Select(_ => Guid.CreateVersion7())];
        _userLookup = userIds.Select((id, n) => (id, n)).ToDictionary(x => x.id, x => $"User {x.n:D2}");

        // RowVersion is assigned by AppDbContext on save; set it directly so the Base64
        // ETag conversion is part of what's measured, as it is for every persisted task.
        PropertyInfo rowVersion = typeof(ProjectTask).GetProperty(nameof(ProjectTask.RowVersion))!;

        _tasks = new List<ProjectTask>(TaskCount);
        for (int i = 0; i < TaskCount; i++)
        {
            var task = ProjectTask.Create(
                boardId: Guid.CreateVersion7(),
                reporterId: userIds[i % userIds.Length],
                title: $"Task #{i}",
                now: now,
                priority: (Priority)(i % 4),
                description: $"Description for task {i}.",
                dueDate: now.AddDays(1 + (i % 30))); // the domain only accepts future due dates

            task.Assign(userIds[(i + 1) % userIds.Length], userIds[0], now);

            for (int c = 0; c < 3; c++)
            {
                task.AddComment($"Comment {c} on task {i}.", userIds[(i + c) % userIds.Length], now);
            }

            for (int a = 0; a < 2; a++)
            {
                task.AddAttachment($"file-{i}-{a}.pdf", "application/pdf", 2_048, $"/attachments/{i}-{a}.pdf", userIds[0], now);
            }

            rowVersion.SetValue(task, BitConverter.GetBytes((long)i));
            _tasks.Add(task);
        }

        _mapsterConfig = MapsterTaskMapping.CreateConfig();

        string manualJson = JsonSerializer.Serialize(Manual());
        string mapsterJson = JsonSerializer.Serialize(Mapster());
        if (manualJson != mapsterJson)
        {
            throw new InvalidOperationException("Mapster configuration does not produce the same TaskDto graph as TaskMapper.");
        }
    }

    /// <summary>The production hand-written mapper.</summary>
    [Benchmark(Baseline = true, Description = "Manual (TaskMapper)")]
    public List<TaskDto> Manual()
    {
        var result = new List<TaskDto>(_tasks.Count);
        foreach (ProjectTask task in _tasks)
        {
            result.Add(task.ToDto(_userLookup));
        }

        return result;
    }

    /// <summary>Mapster mapping the whole list in one adapter call (its most efficient usage).</summary>
    [Benchmark(Description = "Mapster")]
    public List<TaskDto> Mapster()
        => _tasks
            .BuildAdapter(_mapsterConfig)
            .AddParameters(MapsterTaskMapping.UserLookupParameter, _userLookup)
            .AdaptToType<List<TaskDto>>();
}
