using Ordinis.Application.Common;

namespace Ordinis.UnitTests.Application.Common;

/// <summary>
/// Verifies <see cref="PageRequest.Normalize"/> — the single rule every paginated list query uses
/// to make client-supplied <c>page</c>/<c>pageSize</c> safe (GAP-01).
/// </summary>
public class PageRequestTests
{
    [Theory]
    [InlineData(1, 20, 1, 20)]
    [InlineData(3, 50, 3, 50)]
    [InlineData(1, 100, 1, 100)]
    public void Normalize_ValidValues_AreKept(int page, int pageSize, int expectedPage, int expectedSize)
    {
        PageRequest paging = PageRequest.Normalize(page, pageSize);

        Assert.Equal(expectedPage, paging.Page);
        Assert.Equal(expectedSize, paging.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void Normalize_PageSizeBelowOne_BecomesOne(int pageSize)
    {
        Assert.Equal(1, PageRequest.Normalize(1, pageSize).PageSize);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(500)]
    [InlineData(int.MaxValue)]
    public void Normalize_PageSizeAboveMax_BecomesMax(int pageSize)
    {
        Assert.Equal(PageRequest.MaxPageSize, PageRequest.Normalize(1, pageSize).PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Normalize_PageBelowOne_BecomesFirstPage(int page)
    {
        PageRequest paging = PageRequest.Normalize(page, 20);

        Assert.Equal(1, paging.Page);
        Assert.Equal(0, paging.Skip);
    }

    [Fact]
    public void Skip_IsOffsetOfRequestedPage()
    {
        Assert.Equal(40, PageRequest.Normalize(3, 20).Skip);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    public void Normalize_HugePage_DoesNotOverflowSkip(int pageSize)
    {
        PageRequest paging = PageRequest.Normalize(int.MaxValue, pageSize);

        // Unchecked arithmetic would wrap (int.MaxValue - 1) * pageSize to a negative offset.
        Assert.InRange(paging.Skip, 0, int.MaxValue);
        Assert.Equal((long)(paging.Page - 1) * paging.PageSize, paging.Skip);
    }
}
