using RimworldStatistics.VfeInsectoids;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeInsectoidsBirthTests
{
    [Theory]
    [InlineData(3, 4, true, true, true, true, true)]
    [InlineData(3, 4, false, true, true, true, false)]
    [InlineData(3, 4, true, false, true, true, false)]
    [InlineData(3, 4, true, true, false, true, false)]
    [InlineData(3, 4, true, true, true, false, false)]
    [InlineData(3, 3, true, true, true, true, false)]
    [InlineData(3, 5, true, true, true, true, false)]
    public void OnlyFreshSingleSpawnFromNormalHiveTickCounts(
        int before, int after, bool pawnSpawned, bool owner, bool sameMap,
        bool normalTickScope, bool expected)
        => Assert.Equal(expected, VfeInsectoidsRules.NewBirth(
            before, after, pawnSpawned, owner, sameMap, normalTickScope));

    [Fact]
    public void RepeatedGenuineBirthsKeepQualifiedKindCounts()
    {
        var family = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true };
        var key = OptionalTransitionRecording.QualifiedDefKey("VFEInsectoid2", "WorkerKind");
        OptionalTransitionRecording.RecordCount(family, "spawned", key: key, withBreakdown: true);
        OptionalTransitionRecording.RecordCount(family, "spawned", key: key, withBreakdown: true);
        Assert.Equal(2L, family.Peek("spawned")!.N);
        Assert.Equal(2d, family.Peek("spawned")!.By![key!]);
    }
}
