using RimworldStatistics.VnpeGrinder;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class VnpeGrinderStatisticsTests
{
    [Theory]
    [InlineData(1f, true, true, true, 1d)]
    [InlineData(0.25f, true, true, true, 0.25d)]
    [InlineData(0f, true, true, true, 0d)]
    [InlineData(1f, false, true, true, 0d)]
    [InlineData(1f, true, false, true, 0d)]
    [InlineData(1f, true, true, false, 0d)]
    [InlineData(float.NaN, true, true, true, 0d)]
    [InlineData(float.PositiveInfinity, true, true, true, 0d)]
    public void CountOnlyConfirmedGrinderStoredPaste(
        float amount, bool productionSucceeded, bool netMatches, bool playerOwned, double expected)
        => Assert.Equal(expected, VnpeGrinderRules.AcceptedPaste(
            amount, productionSucceeded, netMatches, playerOwned), 6);

    [Fact]
    public void RealPastureVolumesStayFractional()
    {
        var s = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true };
        Assert.True(OptionalTransitionRecording.RecordReal(s, "stored", 0.25));
        Assert.True(OptionalTransitionRecording.RecordReal(s, "stored", 0.75));
        Assert.Equal(1d, s.Peek("stored")!.Sum!.Value, 6);
    }
}
