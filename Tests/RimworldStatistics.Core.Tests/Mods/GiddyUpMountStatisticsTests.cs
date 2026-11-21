using RimworldStatistics.GiddyUp;

namespace RimworldStatistics.Core.Tests;

public sealed class GiddyUpMountStatisticsTests
{
    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    public void CountOnlyOwnedRealRidingTransitionFromCompletedMountJob(
        bool wasMounted, bool isMounted, bool animalMatches, bool ordinaryJob, bool expected)
        => Assert.Equal(expected, GiddyUpMountRules.Completed(
            wasMounted, isMounted, animalMatches, ordinaryJob));
}
