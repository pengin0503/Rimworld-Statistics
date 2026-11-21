using RimworldStatistics.PopularModOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class RaceBirthOutcomeTests
{
    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(false, true, true, true, true, false)] // failed spawn
    [InlineData(true, false, true, true, true, false)] // already-hatched egg
    [InlineData(true, true, false, true, true, false)] // no player home map
    [InlineData(true, true, true, false, true, false)] // not a live player pawn
    [InlineData(true, true, true, true, false, false)] // exception or uncommitted birth
    public void OnlyCommittedLivePlayerBirthsCount(bool returned, bool newChild,
        bool home, bool livePlayerHuman, bool completed, bool expected)
        => Assert.Equal(expected, RaceBirthRules.Committed(
            returned, newChild, home, livePlayerHuman, completed));
}
