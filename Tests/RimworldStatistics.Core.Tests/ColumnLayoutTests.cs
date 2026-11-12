namespace RimworldStatistics.Core.Tests;

public sealed class ColumnLayoutTests
{
    [Fact]
    public void ExtraSpaceIsSharedByWeight()
    {
        var result = ColumnLayout.Resolve([new(100, 1), new(100, 3)], 400);
        Assert.Equal(new[] { 150f, 250f }, result.Widths);
        Assert.Equal(400, result.TotalWidth);
        Assert.False(result.NeedsHorizontalScroll(400));
    }

    [Fact]
    public void OverflowKeepsMinimumsAndScrolls()
    {
        var result = ColumnLayout.Resolve([new(300, 1), new(300, 1)], 400);
        Assert.Equal(new[] { 300f, 300f }, result.Widths);
        Assert.Equal(600, result.TotalWidth);
        Assert.True(result.NeedsHorizontalScroll(400));
    }

    [Fact]
    public void ZeroWeightsShareEqually() =>
        Assert.Equal(new[] { 70f, 90f }, ColumnLayout.Resolve([new(20, 0), new(40, 0)], 160).Widths);

    [Theory]
    [InlineData(0)]
    [InlineData(-20)]
    public void NonPositiveAvailableReturnsMinimums(float available) =>
        Assert.Equal(new[] { 20f, 40f }, ColumnLayout.Resolve([new(20, 1), new(40, 1)], available).Widths);

    [Fact]
    public void SingleColumnFillsWidth() =>
        Assert.Equal(new[] { 400f }, ColumnLayout.Resolve([new(100, 0)], 400).Widths);
}
