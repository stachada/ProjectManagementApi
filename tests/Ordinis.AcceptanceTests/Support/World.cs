namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// Per-scenario state shared by all step classes (Reqnroll injects one instance per scenario).
/// Gherkin refers to things by name ("Bob", project "Apollo", task "Fix login bug"); this maps
/// those names to the IDs the API uses, and remembers what a scenario has observed so far.
/// </summary>
internal sealed class World
{
    /// <summary>A named user from the feature's persona list.</summary>
    internal sealed record Persona(string Name, Guid UserId, Guid OrganizationId);

    public Dictionary<string, Guid> Organizations { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Persona> Personas { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Projects { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Boards { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Tasks { get; } = new(StringComparer.Ordinal);

    /// <summary>Task IDs in the order they were created, for "listed in creation order" checks.</summary>
    public List<Guid> TaskCreationOrder { get; } = [];

    /// <summary>Uploaded attachments: (task ID, file name) → attachment ID.</summary>
    public Dictionary<(Guid TaskId, string FileName), Guid> Attachments { get; } = new();

    /// <summary>
    /// Hidden user that "created" each seeded project (the domain makes the creator its first
    /// Admin). Removed from the project as soon as a named Admin joins, so membership lists only
    /// show the personas the feature describes.
    /// </summary>
    public Dictionary<Guid, Guid> ProjectFounders { get; } = new();

    /// <summary>Organization of each project, by project ID.</summary>
    public Dictionary<Guid, Guid> ProjectOrganizations { get; } = new();

    /// <summary>Project of each board, by board ID.</summary>
    public Dictionary<Guid, Guid> BoardProjects { get; } = new();

    /// <summary>Versions (ETags) a persona has read: (persona, resource ID) → quoted ETag.</summary>
    public Dictionary<(string Persona, Guid ResourceId), string> ReadVersions { get; } = new();

    /// <summary>The response to the most recent "When" action.</summary>
    public ApiResponse? LastResponse { get; set; }

    public ApiResponse Response =>
        LastResponse ?? throw new InvalidOperationException("No request has been made in this scenario yet.");

    public Persona PersonaNamed(string name) =>
        Personas.TryGetValue(name, out Persona? persona)
            ? persona
            : throw new InvalidOperationException($"Persona '{name}' has not been set up in this scenario.");

    public Guid TaskNamed(string title) => Lookup(Tasks, title, "task");

    public Guid ProjectNamed(string name) => Lookup(Projects, name, "project");

    public Guid BoardNamed(string name) => Lookup(Boards, name, "board");

    public Guid OrganizationNamed(string name) => Lookup(Organizations, name, "organization");

    private static Guid Lookup(Dictionary<string, Guid> map, string key, string kind) =>
        map.TryGetValue(key, out Guid id)
            ? id
            : throw new InvalidOperationException($"No {kind} named '{key}' has been set up in this scenario.");
}
