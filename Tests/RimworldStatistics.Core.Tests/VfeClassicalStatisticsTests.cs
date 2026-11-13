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

    [Fact]
    public void TwoSenatorsOfOneFactionProduceTwoFavorOutcomes()
    {
        var s = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 10, Continuous = true };
        var key = OptionalTransitionRecording.QualifiedDefKey("example.faction", "RomanRepublic");
        OptionalTransitionRecording.RecordCount(s, "favor", key: key, withBreakdown: true);
        OptionalTransitionRecording.RecordCount(s, "favor", key: key, withBreakdown: true);
        Assert.Equal(2L, s.Peek("favor")!.N);
        Assert.Equal(2d, s.Peek("favor")!.By![key!]);
    }
}
