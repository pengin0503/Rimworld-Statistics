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

    [Fact]
    public void BreakdownRecordsEachBirthWithoutChangingOtherMetricKeys()
    {
        var state = new FamilyState
        {
            Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true,
        };
        var first = OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld.Biotech", "Saurid");
        var second = OptionalTransitionRecording.QualifiedDefKey("Another.Xenotype", "Other");
        Assert.True(OptionalTransitionRecording.RecordCount(state, "completed", key: first, withBreakdown: true));
        Assert.True(OptionalTransitionRecording.RecordCount(state, "completed", key: second, withBreakdown: true));
        Assert.True(OptionalTransitionRecording.RecordCount(state, "completed", key: first, withBreakdown: true));
        var output = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        const string key = "mod.vanillaracesexpanded.saurid.egg_births.completed";
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(state, 100, output),
            "completed", key, withBreakdown: true);
        Assert.Equal(3L, output[key].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, output[key].Values[first!].IntegerValue);
        Assert.Equal(1L, output[key].Values[second!].IntegerValue);
        Assert.Single(output);
    }

    [Fact]
    public void UnknownCustomXenotypeIncreasesTotalButMakesBreakdownPartial()
    {
        var state = new FamilyState
        {
            Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true,
        };
        OptionalTransitionRecording.RecordCount(state, "completed", key: null, withBreakdown: true);
        var output = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        const string key = "mod.vanillaracesexpanded.phytokin.sapling_births.completed";
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(state, 100, output),
            "completed", key, withBreakdown: true);
        Assert.Equal(1L, output[key].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, output[key].Values[MetricEntry.Scalar].Coverage);
    }
}
