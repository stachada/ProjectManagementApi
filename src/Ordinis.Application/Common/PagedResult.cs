namespace Ordinis.Application.Common;

/// <summary>
/// Wraps a paginated list of items with metadata needed by the API layer
/// to construct <c>X-Total-Count</c> headers and next/prev link generation.
/// </summary>
/// <typeparam name="T">The item type returned in this page.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>
    /// The items on the current page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Total number of items across all pages, before pagination is applied.
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    /// The 1-based page number returned.
    /// </summary>
    public int Page { get; }

    /// <summary>
    /// Maximum number of items per page that was requested.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Total number of pages given <see cref="TotalCount"/> and <see cref="PageSize"/>.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>
    /// Whether a page before this one exists.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Whether a page after this one exists.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <param name="items">The items on the current page.</param>
    /// <param name="totalCount">Total items across all pages.</param>
    /// <param name="page">Current 1-based page number.</param>
    /// <param name="pageSize">Requested page size.</param>
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    /// Projects each item to a new type. preserving all pagination metadata.
    /// Useful when handler returns <c>PagedResult<Entity></c> that needs
    /// to be mapped to <c>PagedResult<Dto></c> without re-running the query.
    /// </summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
        => new (Items.Select(selector).ToList(), TotalCount, Page, PageSize);
}
