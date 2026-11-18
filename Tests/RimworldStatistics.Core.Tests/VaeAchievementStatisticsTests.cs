using RimworldStatistics.VaeAchievements;

namespace RimworldStatistics.Core.Tests;

public sealed class VaeAchievementStatisticsTests
{
    [Theory]
    [InlineData(false, true, false, false, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, false, true, false)]
    public void UnlockCount_OnlyNewNonDebugNormalPlay(
        bool wasUnlocked, bool isUnlocked, bool debug, bool initializing, bool expected)
        => Assert.Equal(expected,
            VaeAchievementRules.NewUnlock(wasUnlocked, isUnlocked, debug, initializing));

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(20, 20, 0)]
    [InlineData(20, 19, null)]
    [InlineData(null, 21, null)]
    [InlineData(5, null, null)]
    public void Points_OnlyPositiveOrZeroMeasuredCredit(int? before, int? after, int? expected)
        => Assert.Equal(expected, VaeAchievementRules.CreditedPoints(before, after));
}
