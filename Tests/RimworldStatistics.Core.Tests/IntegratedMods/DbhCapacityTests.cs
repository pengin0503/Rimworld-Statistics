using RimworldStatistics.DubsBadHygieneAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class DbhCapacityTests
{
    [Fact]
    public void DeduplicatesSamePlumbingNetworkBeforeSummingRates()
    {
        var a = new object();
        var b = new object();
        var sum = new DbhCapacityAccumulator();
        Assert.True(sum.Add(a, 4000f));
        Assert.True(sum.Add(a, 4000f));
        Assert.True(sum.Add(b, 2000.5f));
        Assert.Equal(6000.5, sum.Value);
    }

    [Fact]
    public void KnownNetworkWithZeroCapacityIsMeasuredZero()
    {
        var sum = new DbhCapacityAccumulator();
        Assert.True(sum.Add(new object(), 0));
        Assert.Equal(0.0, sum.Value);
    }

    [Fact]
    public void MissingNetworksNeverPretendThereIsZeroCapacity()
        => Assert.Null(new DbhCapacityAccumulator().Value);

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1.001e12f)]
    public void InvalidSourcePoisonedDoesNotMakePartialCapacityAppearValid(float invalid)
    {
        var sum = new DbhCapacityAccumulator();
        Assert.True(sum.Add(new object(), 10));
        Assert.False(sum.Add(new object(), invalid));
        Assert.Null(sum.Value);
    }
}