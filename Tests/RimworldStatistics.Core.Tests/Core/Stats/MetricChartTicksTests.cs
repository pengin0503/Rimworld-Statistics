namespace RimworldStatistics.Core.Tests;

public sealed class MetricChartTicksTests
{
    [Fact]
    public void ZeroOnlyRangeReturnsFiniteZeroBasedTicks()
    {
        var ticks = MetricChartTicks.Generate(0d, 0d);
        Assert.True(ticks.Count >= 2);
        Assert.Contains(0d, ticks);
        Assert.Equal(0d, ticks[0]);
        Assert.True(ticks[^1] > 0d);
        Assert.All(ticks, t => Assert.True(double.IsFinite(t)));
    }

    [Fact]
    public void PositiveOnlySeriesIncludesZeroAndCoversMax()
    {
        var ticks = MetricChartTicks.Generate(4d, 37d);
        Assert.Equal(0d, ticks[0]);
        Assert.True(ticks[^1] >= 37d);
        AssertStrictlyAscending(ticks);
    }

    [Fact]
    public void NegativeOnlySeriesIncludesZeroAtTopEndAndCoversMin()
    {
        var ticks = MetricChartTicks.Generate(-42d, -5d);
        Assert.True(ticks[0] <= -42d);
        Assert.Equal(0d, ticks[^1]);
        AssertStrictlyAscending(ticks);
    }

    [Fact]
    public void MixedSignSeriesContainsExactZeroAndCoversBothEnds()
    {
        var ticks = MetricChartTicks.Generate(-17d, 43d);
        Assert.True(ticks[0] <= -17d);
        Assert.Contains(0d, ticks);
        Assert.True(ticks[^1] >= 43d);
        AssertStrictlyAscending(ticks);
    }

    [Fact]
    public void SinglePointSeriesUsesZeroAndSingleValueBounds()
    {
        var ticks = MetricChartTicks.Generate(120d, 120d);
        Assert.Equal(0d, ticks[0]);
        Assert.True(ticks[^1] >= 120d);
        AssertStrictlyAscending(ticks);
    }

    [Theory]
    [InlineData(0d, 3e-9)]
    [InlineData(-4e-9, 2e-9)]
    [InlineData(0d, 1e12)]
    [InlineData(-5e11, 5e11)]
    public void ExtremeMagnitudesRemainFiniteAndCoverInputRange(double min, double max)
    {
        var ticks = MetricChartTicks.Generate(min, max, 5);
        Assert.True(ticks.Count >= 2);
        Assert.Contains(0d, ticks);
        Assert.True(ticks[0] <= Math.Min(0d, min));
        Assert.True(ticks[^1] >= Math.Max(0d, max));
        Assert.All(ticks, t => Assert.True(double.IsFinite(t)));
        AssertStrictlyAscending(ticks);
    }

    [Fact]
    public void NonFiniteInputsFallBackSafelyWithoutNaNOrInfinity()
    {
        var ticks = MetricChartTicks.Generate(double.NaN, double.PositiveInfinity);
        Assert.True(ticks.Count >= 2);
        Assert.Contains(0d, ticks);
        Assert.All(ticks, t => Assert.True(double.IsFinite(t)));
    }

    [Fact]
    public void ExtremeHugeOppositeSignsNeverOverflowStepToInfinity()
    {
        var ticks = MetricChartTicks.Generate(-1e300, 1e300, 5);
        Assert.True(ticks.Count >= 2);
        Assert.Contains(0d, ticks);
        Assert.All(ticks, t => Assert.True(double.IsFinite(t)));
        AssertStrictlyAscending(ticks);
    }

    [Fact]
    public void FormattingPreservesZeroAndTinyNonZeroWithoutRoundingToZero()
    {
        Assert.Equal("0", MetricChartTicks.FormatAxisValue(0d));
        Assert.Equal("0%", MetricChartTicks.FormatAxisValue(0d, percent: true));
        Assert.Equal("50%", MetricChartTicks.FormatAxisValue(0.5d, percent: true));
        Assert.DoesNotMatch("^0%?$", MetricChartTicks.FormatAxisValue(1e-9));
    }

    private static void AssertStrictlyAscending(IReadOnlyList<double> ticks)
    {
        for (var i = 1; i < ticks.Count; i++)
            Assert.True(ticks[i] > ticks[i - 1], $"Expected tick[{i}] ({ticks[i]}) > tick[{i - 1}] ({ticks[i - 1]})");
    }
}
