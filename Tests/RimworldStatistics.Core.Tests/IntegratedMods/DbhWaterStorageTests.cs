using RimworldStatistics.DubsBadHygieneAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class DbhWaterStorageTests
{
    [Fact]
    public void CountsEachPhysicalTowerOnlyOnceAcrossRepeatedNetworkReferences()
    {
        var sum = new DbhStorageAccumulator();
        var tower1 = new object();
        var tower2 = new object();
        Assert.True(sum.Add(tower1, 1200.5f));
        Assert.True(sum.Add(tower2, 300.25f));
        Assert.True(sum.Add(tower1, 1200.5f));
        Assert.Equal(1500.75, sum.Value);
        Assert.Equal(2, sum.DistinctTowers);
    }

    [Fact]
    public void LegitimateEmptyTankCanBeMeasuredAsZero()
    {
        var sum = new DbhStorageAccumulator();
        Assert.True(sum.Add(new object(), 0f));
        Assert.Equal(0.0, sum.Value);
    }

    [Fact]
    public void NoHomeMapTankEvidenceRemainsUnknownNotZero()
        => Assert.Null(new DbhStorageAccumulator().Value);

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void CorruptedTankPoisonsAggregateRatherThanBeingSilentlyDropped(float invalid)
    {
        var sum = new DbhStorageAccumulator();
        Assert.True(sum.Add(new object(), 100f));
        Assert.False(sum.Add(new object(), invalid));
        Assert.Null(sum.Value);
    }

    [Fact]
    public void IncompleteOrOverlargeReadingsCannotInventValue()
    {
        var sum = new DbhStorageAccumulator();
        Assert.False(sum.Add(null, 25f));
        Assert.Null(sum.Value);
        var tooLarge = new DbhStorageAccumulator();
        Assert.False(tooLarge.Add(new object(), 1.001e12f));
        Assert.Null(tooLarge.Value);
    }
}
