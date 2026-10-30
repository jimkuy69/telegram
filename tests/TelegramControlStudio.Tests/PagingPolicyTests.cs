using TelegramControlStudio.Core.Operations;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class PagingPolicyTests
{
    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(1, 100, 1)]
    [InlineData(100, 100, 1)]
    [InlineData(101, 100, 2)]
    [InlineData(250, 100, 3)]
    public void TotalPagesRoundsUpAndNeverReturnsZero(int itemCount, int pageSize, int expected)
    {
        Assert.Equal(expected, PagingPolicy.TotalPages(itemCount, pageSize));
    }

    [Fact]
    public void TotalPagesRejectsNonPositivePageSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagingPolicy.TotalPages(10, 0));
    }

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(1, 3, 1)]
    [InlineData(3, 3, 3)]
    [InlineData(9, 3, 3)]
    public void ClampKeepsPageInsideRange(int page, int totalPages, int expected)
    {
        Assert.Equal(expected, PagingPolicy.Clamp(page, totalPages));
    }

    [Fact]
    public void SliceReturnsRemainderOnLastPage()
    {
        Assert.Equal((0, 100), PagingPolicy.Slice(1, 100, 250));
        Assert.Equal((100, 100), PagingPolicy.Slice(2, 100, 250));
        Assert.Equal((200, 50), PagingPolicy.Slice(3, 100, 250));
    }

    [Fact]
    public void SliceClampsOutOfRangePage()
    {
        Assert.Equal((200, 50), PagingPolicy.Slice(99, 100, 250));
        Assert.Equal((0, 100), PagingPolicy.Slice(0, 100, 250));
    }

    [Fact]
    public void SliceOnEmptyListIsEmpty()
    {
        Assert.Equal((0, 0), PagingPolicy.Slice(1, 100, 0));
    }

    [Fact]
    public void WindowShowsAllPagesWhenFewerThanButtons()
    {
        Assert.Equal(new[] { 1, 2, 3 }, PagingPolicy.Window(2, 3));
    }

    [Fact]
    public void WindowCentersCurrentPageInMiddle()
    {
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, PagingPolicy.Window(5, 10));
    }

    [Fact]
    public void WindowPinsToStartAndEndEdges()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, PagingPolicy.Window(1, 10));
        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, PagingPolicy.Window(10, 10));
    }

    [Fact]
    public void WindowReturnsEmptyWhenThereAreNoPages()
    {
        Assert.Empty(PagingPolicy.Window(1, 0));
    }
}
