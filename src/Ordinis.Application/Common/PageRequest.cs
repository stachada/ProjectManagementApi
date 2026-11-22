namespace Ordinis.Application.Common;

/// <summary>
/// The one definition of how a requested page number and size are made safe for a paginated
/// query: every list query normalizes its filter's <c>page</c>/<c>pageSize</c> through
/// <see cref="Normalize"/> instead of clamping inline.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PageSize"/> is clamped to <c>1..<see cref="MaxPageSize"/></c>: a size of zero or
/// less would otherwise reach SQL as <c>FETCH NEXT 0 ROWS</c> / a negative <c>Take</c>, which SQL
/// Server rejects (a 500, not an empty page).
/// </para>
/// <para>
/// <see cref="Page"/> is at least 1 and at most the largest page whose <see cref="Skip"/> still
/// fits in an <see cref="int"/>, so <c>?page=2147483647</c> yields an empty page rather than an
/// overflowed, negative offset.
/// </para>
/// </remarks>
public readonly record struct PageRequest
{
    /// <summary>Page size used when the client does not ask for one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Largest page size a client may request.</summary>
    public const int MaxPageSize = 100;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>1-based page number, always at least 1.</summary>
    public int Page { get; }

    /// <summary>Items per page, always within <c>1..<see cref="MaxPageSize"/></c>.</summary>
    public int PageSize { get; }

    /// <summary>Number of items to skip — never negative, never overflowed.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>Clamps a client-supplied page number and size into safe, valid values.</summary>
    public static PageRequest Normalize(int page, int pageSize)
    {
        int size = Math.Clamp(pageSize, 1, MaxPageSize);

        // (lastPage - 1) * size <= int.MaxValue - size, so Skip cannot overflow.
        int lastAddressablePage = int.MaxValue / size;

        return new PageRequest(Math.Clamp(page, 1, lastAddressablePage), size);
    }
}
