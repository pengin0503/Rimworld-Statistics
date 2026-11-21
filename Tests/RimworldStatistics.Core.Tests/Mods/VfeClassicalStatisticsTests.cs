using RimworldStatistics.VfeClassical;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeClassicalStatisticsTests
{
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    public void ConfirmedFavorRequiresNewStateTransitionAndNormalPlay(
        bool before, bool after, bool normalPlay, bool expected)
        => Assert.Equal(expected, VfeClassicalRules.FavorGained(before, after, normalPlay));
}
