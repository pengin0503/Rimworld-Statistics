using RimworldStatistics.VfeTribals;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeTribalsStatisticsTests
{
    [Theory]
    [InlineData(0, 1, 1, true)]
    [InlineData(1, 2, 2, true)]
    [InlineData(2, 2, 2, false)]
    [InlineData(2, 1, 1, false)]
    [InlineData(1, 2, 3, false)]
    [InlineData(null, 1, 1, false)]
    public void EraOnlyCountsRealForwardProgress(int? before, int after, int target, bool expected)
        => Assert.Equal(expected, VfeTribalsRules.EraAdvanced(before, after, target));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void CornerstoneOnlyCountsNewAcquisition(bool previous, bool next, bool expected)
        => Assert.Equal(expected, VfeTribalsRules.NewCornerstone(previous, next));

    [Fact]
    public void EraAndCornerstoneBreakdownsDoNotClobberOneAnother()
    {
        var family = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 10, Continuous = true };
        OptionalTransitionRecording.RecordCount(family, "eras", 1, "example.mod/Era1", withBreakdown: true);
        OptionalTransitionRecording.RecordCount(family, "cornerstones", 1, "example.mod/Stone1", withBreakdown: true);
        Assert.Equal(1L, family.Peek("eras")!.N);
        Assert.Equal(1L, family.Peek("cornerstones")!.N);
        Assert.Equal("example.mod/Era1", Assert.Single(family.Peek("eras")!.By!).Key);
        Assert.Equal("example.mod/Stone1", Assert.Single(family.Peek("cornerstones")!.By!).Key);
    }
}
