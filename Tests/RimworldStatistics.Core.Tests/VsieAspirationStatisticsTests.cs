using RimworldStatistics.Vsie;

namespace RimworldStatistics.Core.Tests;

public sealed class VsieAspirationStatisticsTests
{
    [Theory]
    [InlineData(1, 2, 2, true, true, true)]
    [InlineData(0, 1, 2, true, true, false)]
    [InlineData(1, 2, 2, false, true, false)]
    [InlineData(1, 2, 2, true, false, false)]
    [InlineData(2, 2, 2, true, true, false)]
    public void CompleteOnlyAtObservedThresholdAndRemovedFromActive(
        int before, int after, int threshold, bool removed, bool inspirationMatches,
        bool expected)
        => Assert.Equal(expected, VsieAspirationRules.Completed(
            before, after, threshold, removed, inspirationMatches));
}
