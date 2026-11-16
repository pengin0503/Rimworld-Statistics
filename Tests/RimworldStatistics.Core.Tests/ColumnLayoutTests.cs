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

    [Fact]
    public void VisibleColumnsSkipOffscreenColumnsOnBothSides() =>
        Assert.Equal((1, 3), ColumnLayout.VisibleColumns([100f, 100f, 100f, 100f], 150f, 110f));

    [Fact]
    public void VisibleColumnsRespectExactViewportBoundaries() =>
        Assert.Equal((1, 2), ColumnLayout.VisibleColumns([100f, 100f, 100f], 100f, 100f));

    [Fact]
    public void VisibleColumnsIncludeAllColumnsWithoutHorizontalScroll() =>
        Assert.Equal((0, 3), ColumnLayout.VisibleColumns([90f, 110f, 100f], 0f, 300f));

    [Fact]
    public void VisibleColumnsAfterLastColumnReturnEmptyEndRange() =>
        Assert.Equal((3, 3), ColumnLayout.VisibleColumns([100f, 100f, 100f], 500f, 100f));

    [Fact]
    public void VisibleColumnsWithNonPositiveViewportReturnEmptyRange() =>
        Assert.Equal((0, 0), ColumnLayout.VisibleColumns([100f, 100f], 0f, 0f));

    [Fact]
    public void VisibleColumnsWithNegativeScrollClampToTableStart() =>
        Assert.Equal((0, 2), ColumnLayout.VisibleColumns([100f, 100f, 100f], -10f, 150f));

    [Theory]
    [InlineData(940f, 1, false)]
    [InlineData(940f, 2, false)]
    [InlineData(940f, 4, false)]
    [InlineData(620f, 4, true)]
    [InlineData(360f, 2, true)]
    public void ResolvePinnedKeepsPositiveNameAndDataViewportAndScrollsOnlyWhenNeeded(
        float totalWidth, int dataColumns, bool expectScroll)
    {
        var plan = ColumnLayout.ResolvePinned(totalWidth, dataColumns);
        Assert.True(plan.NameWidth > 0f);
        Assert.True(plan.DataViewportWidth > 0f);
        Assert.Equal(totalWidth - 16f, plan.NameWidth + plan.DataViewportWidth, precision: 2);
        Assert.Equal(expectScroll, plan.NeedsHorizontalScroll);
        Assert.True(plan.DataTotalWidth >= plan.DataViewportWidth - 0.5f);
    }

    [Theory]
    [InlineData(0f, 2)]
    [InlineData(16f, 2)]
    [InlineData(500f, 0)]
    public void ResolvePinnedWithDegenerateDimensionsDoesNotProduceNaNOrNegative(float totalWidth, int dataColumns)
    {
        var plan = ColumnLayout.ResolvePinned(totalWidth, dataColumns);
        Assert.Equal(0f, plan.NameWidth);
        Assert.Equal(0f, plan.DataViewportWidth);
        Assert.Equal(0f, plan.ColumnWidth);
        Assert.False(plan.NeedsHorizontalScroll);
    }

    [Theory]
    [InlineData(4, 100f, 150f, 110f)]
    [InlineData(3, 100f, 100f, 100f)]
    [InlineData(3, 100f, 0f, 300f)]
    [InlineData(3, 100f, 500f, 100f)]
    [InlineData(2, 100f, 0f, 0f)]
    [InlineData(3, 100f, -10f, 150f)]
    public void VisibleUniformColumnsMatchVisibleColumnsRange(
        int count, float width, float viewportStart, float viewportWidth)
    {
        var widths = Enumerable.Repeat(width, count).ToArray();
        Assert.Equal(
            ColumnLayout.VisibleColumns(widths, viewportStart, viewportWidth),
            ColumnLayout.VisibleUniformColumns(count, width, viewportStart, viewportWidth));
    }
}
