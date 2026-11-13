using RimworldStatistics.VseExpertise;

namespace RimworldStatistics.Core.Tests;

public sealed class VseExpertiseStatisticsTests
{
    [Theory]
    [InlineData(0, 1, true, true)]
    [InlineData(1, 1, true, false)]
    [InlineData(0, 1, false, false)]
    [InlineData(2, 3, false, false)]
    public void OnlyNormalSelectedNewExpertiseCounts(int before, int after, bool panel, bool expected)
        => Assert.Equal(expected, VseExpertiseRules.NewSelection(before, after, panel));

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(8, 10, 2)]
    [InlineData(19, 20, 1)]
    [InlineData(8, 8, 0)]
    [InlineData(10, 9, 0)]
    public void LevelGainsArePositiveRealLevelTransitions(int before, int after, long expected)
        => Assert.Equal(expected, VseExpertiseRules.EarnedLevels(before, after));

    [Fact]
    public void HighestExpertiseLevelIsMaximumRatherThanCumulativeSum()
    {
        var state = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true };
        OptionalTransitionRecording.RecordReal(state, "maxLevel", 8);
        OptionalTransitionRecording.RecordReal(state, "maxLevel", 12);
        OptionalTransitionRecording.RecordReal(state, "maxLevel", 10);
        Assert.Equal(12d, state.Peek("maxLevel")!.Max);
    }
}
