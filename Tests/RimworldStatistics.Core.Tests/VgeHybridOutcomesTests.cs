using RimworldStatistics.VgeHybridOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class VgeHybridOutcomesTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    public void WombBirthOnlyCountsSuccessfulSpawnedPlayerHybrid(
        bool ready, bool noFailure, bool owned, bool spawned, bool expected)
        => Assert.Equal(expected, VgeHybridOutcomeRules.HybridBorn(
            ready, noFailure, owned, spawned));

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, true, true, true, false)]
    public void MechTransformationRequiresDestroyedOriginalAndOneNewMechPawn(
        bool completed, bool originalDestroyed, bool owned, bool spawned, bool expected)
        => Assert.Equal(expected, VgeHybridOutcomeRules.MechTransformed(
            completed, originalDestroyed, owned, spawned));
}
