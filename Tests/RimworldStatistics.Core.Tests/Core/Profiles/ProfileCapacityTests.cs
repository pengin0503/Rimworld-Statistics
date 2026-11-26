namespace RimworldStatistics.Core.Tests;

public sealed class ProfileCapacityTests
{
    private const long Limit = ProfileStore.MaxProfileFileBytes;

    [Fact]
    public void LimitIsTheProfileLoadCap()
    {
        Assert.Equal(256L * 1024 * 1024, Limit);
        Assert.Equal(ProfileStore.MaxProfileFileBytes, ProfileCapacity.LimitBytes);
    }

    [Theory]
    [InlineData(0, ProfileCapacityLevel.Normal)]
    [InlineData(74, ProfileCapacityLevel.Normal)]
    [InlineData(75, ProfileCapacityLevel.Approaching)]
    [InlineData(89, ProfileCapacityLevel.Approaching)]
    [InlineData(90, ProfileCapacityLevel.Critical)]
    [InlineData(100, ProfileCapacityLevel.Critical)]
    public void ClassifiesByFractionOfTheLoadLimit(int percent, ProfileCapacityLevel level)
    {
        var size = (long)Math.Ceiling(Limit * percent / 100d);
        var capacity = ProfileCapacity.ForSize(size);
        Assert.Equal(size, capacity.SizeBytes);
        Assert.Equal(level, capacity.Level);
        Assert.Equal(size / (double)Limit, capacity.Fraction, precision: 12);
        Assert.Equal(level != ProfileCapacityLevel.Normal, capacity.IsWarning);
    }

    [Fact]
    public void IntegerPercentOfTheLimitStillHitsTheThreshold()
    {
        // Callers computing 90% with integer division must not fall just below the threshold.
        Assert.Equal(ProfileCapacityLevel.Critical, ProfileCapacity.ForSize(Limit * 90 / 100).Level);
        Assert.Equal(ProfileCapacityLevel.Approaching, ProfileCapacity.ForSize(Limit * 75 / 100).Level);
    }

    [Fact]
    public void OneByteOverTheLimitIsOverLimit()
    {
        var capacity = ProfileCapacity.ForSize(Limit + 1);
        Assert.Equal(ProfileCapacityLevel.OverLimit, capacity.Level);
        Assert.True(capacity.Fraction > 1d);
        Assert.True(capacity.IsWarning);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void ANegativeSizeCountsAsZero(long size)
    {
        var capacity = ProfileCapacity.ForSize(size);
        Assert.Equal(0, capacity.SizeBytes);
        Assert.Equal(0d, capacity.Fraction);
        Assert.Equal(ProfileCapacityLevel.Normal, capacity.Level);
    }
}
