using RimworldStatistics.PrisonExpandedParole;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class PrisonExpandedCompletionTests
{
    [Theory]
    [InlineData(10f, 9f, 10f, false, true, true, true)]
    [InlineData(10f, 9f, 11f, false, true, true, true)]
    [InlineData(10f, 4f, 6f, false, false, true, false)]
    [InlineData(10f, 9f, 10f, false, false, true, false)]
    [InlineData(10f, 10f, 10f, true, true, true, false)]
    [InlineData(10f, 10f, 10f, false, true, true, false)]
    [InlineData(10f, 9f, 10f, false, true, false, false)]
    [InlineData(0f, 0f, 1f, false, true, true, false)]
    [InlineData(float.NaN, 0f, 10f, false, true, true, false)]
    public void OnlyNewlyServedPlayerPrisonerSentencesCount(float sentenceDays,
        float beforeDays, float afterDays, bool paroleDueBefore, bool paroleDueAfter,
        bool livingPlayerPrisoner, bool expected)
        => Assert.Equal(expected, PrisonExpandedCompletionRules.Served(
            sentenceDays, beforeDays, afterDays, paroleDueBefore, paroleDueAfter,
            livingPlayerPrisoner));

    [Theory]
    [InlineData(true,true,true,true,false,false,false,true)]
    [InlineData(true,true,true,true,true,false,false,false)]
    [InlineData(true,true,true,true,false,true,false,false)]
    [InlineData(true,true,true,true,false,false,true,false)]
    [InlineData(true,true,true,false,false,false,false,false)]
    [InlineData(true,false,true,true,false,false,false,false)]
    [InlineData(false,true,true,true,false,false,false,false)]
    public void PhysicalDepartureRequiresTrackedPrisonerLeavingPlayerHome(
        bool playerPrisoner, bool tracked, bool home, bool spawnedBefore,
        bool spawnedAfter, bool stillHome, bool destroyed, bool expected)
        => Assert.Equal(expected, PrisonExpandedCompletionRules.Departed(
            playerPrisoner, tracked, home, spawnedBefore, spawnedAfter, stillHome, destroyed));

    [Fact]
    public void RepeatedSentenceTransitionDoesNotIncreaseLifetimeCount()
    {
        var state = new FamilyState { Revision=1, SinceTick=0, ThroughTick=600, Continuous=true };
        var first = PrisonExpandedCompletionRules.Served(10, 9, 10, false, true, true);
        var reloaded = PrisonExpandedCompletionRules.Served(10, 10, 10, true, true, true);
        Assert.True(first);
        Assert.False(reloaded);
        OptionalTransitionRecording.RecordCount(state, "sentences");
        Assert.Equal(1, state.Peek("sentences")!.Sum!.Value);
    }
}