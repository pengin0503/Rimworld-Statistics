using RimworldStatistics.VreAndroid;

namespace RimworldStatistics.Core.Tests;

public sealed class VreAndroidAwakeningTests
{
    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    public void OnlyNormalNaturallyAwakenedPlayerAndroidCounts(
        bool before, bool after, bool player, bool naturalTick, bool expected)
        => Assert.Equal(expected, VreAndroidAwakeningRules.Completed(
            before, after, player, naturalTick));
}
