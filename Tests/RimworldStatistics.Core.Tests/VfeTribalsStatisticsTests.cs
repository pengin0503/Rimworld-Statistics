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
}
