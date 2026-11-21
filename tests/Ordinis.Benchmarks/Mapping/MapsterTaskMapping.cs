using Mapster;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Tasks;

namespace Ordinis.Benchmarks.Mapping;

/// <summary>
/// Mapster configuration producing the same <see cref="TaskDto"/> graph as
/// <see cref="TaskMapper.ToDto"/> - used only by <see cref="TaskMappingBenchmarks"/>.
/// </summary>
/// <remarks>
/// <para>
/// Convention-based mapping covers every same-named member; the explicit <c>Map</c> rules are
/// exactly the places <see cref="TaskMapper"/> does something a convention can't: display-name
/// lookups, the Base64 concurrency token, comment soft-delete filtering and ordering, the
/// <c>StorageUrl</c> to <c>DownloadUrl</c> rename, the edited-only <c>UpdatedAt</c>, and HATEOAS
/// links. That list is the honest size of the configuration Mapster would need in production.
/// </para>
/// <para>
/// The per-request <c>userLookup</c> dictionary is passed the idiomatic Mapster way - as a
/// <see cref="MapContext"/> parameter via <c>BuildAdapter().AddParameters(...)</c> - since the
/// compiled mapping itself is a shared singleton.
/// </para>
/// </remarks>
internal static class MapsterTaskMapping
{
    /// <summary><see cref="MapContext"/> parameter key carrying the <c>UserId -&gt; DisplayName</c> lookup.</summary>
    public const string UserLookupParameter = "userLookup";

    /// <summary>Builds and compiles the configuration (compile once, up front - as an app would at startup).</summary>
    public static TypeAdapterConfig CreateConfig()
    {
        var config = new TypeAdapterConfig();

        // HateoasLink and other same-type members are immutable records - copy the reference,
        // as TaskMapper does, rather than deep-cloning them.
        config.Default.ShallowCopyForSameType(true);

        config.NewConfig<Comment, CommentDto>()
            .Map(d => d.AuthorDisplayName, s => LookupDisplayName(s.AuthorId) ?? string.Empty)
            .Map(d => d.UpdatedAt, s => s.IsEdited ? s.UpdatedAt : (DateTimeOffset?)null);

        config.NewConfig<Attachment, AttachmentDto>()
            .Map(d => d.DownloadUrl, s => s.StorageUrl);

        config.NewConfig<ProjectTask, TaskDto>()
            .Map(d => d.AssigneeDisplayName, s => s.AssigneeId.HasValue ? LookupDisplayName(s.AssigneeId.Value) : null)
            .Map(d => d.ConcurrencyToken, s => s.RowVersion != null && s.RowVersion.Length > 0
                ? Convert.ToBase64String(s.RowVersion)
                : string.Empty)
            .Map(d => d.Comments, s => s.Comments
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.CreatedAt)
                .ThenByStableId())
            .Map(d => d.Attachments, s => s.Attachments
                .OrderBy(a => a.UploadedAt)
                .ThenByStableId())
            .Map(d => d.Links, s => TaskMapper.BuildLinks(s.Id, s.Status));

        config.Compile();
        return config;
    }

    private static string? LookupDisplayName(Guid userId)
    {
        var lookup = (IReadOnlyDictionary<Guid, string>)MapContext.Current!.Parameters[UserLookupParameter];
        return lookup.GetValueOrDefault(userId);
    }
}
