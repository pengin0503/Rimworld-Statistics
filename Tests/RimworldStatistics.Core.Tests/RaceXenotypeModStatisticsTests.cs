using RimworldStatistics.AlphaAnimals;
using RimworldStatistics.VreFungoid;
using RimworldStatistics.VreInsector;
using RimworldStatistics.VreLycanthrope;
using RimworldStatistics.VreSanguophage;

namespace RimworldStatistics.Core.Tests;

public sealed class RaceXenotypeModStatisticsTests
{
    private static FamilyState EmptyState(int since = 0, int through = 100) => new()
    {
        Revision = 1,
        SinceTick = since,
        ThroughTick = through,
        Continuous = true,
    };

    [Theory]
    [InlineData(false, true, true, true, true, true)]
    [InlineData(true, false, true, true, true, true)]
    [InlineData(false, false, true, true, true, false)]
    [InlineData(true, true, true, true, true, false)]
    [InlineData(false, true, false, true, true, false)]
    [InlineData(false, true, true, false, true, false)]
    [InlineData(false, true, true, true, false, false)]
    public void VreLycanthropeMorphOnlyCountsCommittedStateAndXenotypeTransition(
        bool wasMorphed,
        bool nowMorphed,
        bool expectedTargetMatched,
        bool xenotypeChanged,
        bool livePlayerPawn,
        bool expected)
    {
        Assert.Equal(expected, VreLycanthropeRules.MorphCommitted(
            wasMorphed, nowMorphed, expectedTargetMatched, xenotypeChanged, livePlayerPawn));
    }

    [Fact]
    public void VreLycanthropeMorphBreakdownKeepsBeastKeyDespiteLeftoverNameAndMarksCustomRevertPartial()
    {
        var morphKey = VreLycanthropeRules.MorphBreakdownKey(
            nowMorphed: true,
            packageId: "VanillaRacesExpanded.Lycanthrope",
            defName: "VRE_Wolfman",
            xenotypeName: "Custom Pack Leader");
        Assert.Equal("vanillaracesexpanded.lycanthrope/VRE_Wolfman", morphKey);

        var namedRevertKey = VreLycanthropeRules.MorphBreakdownKey(
            nowMorphed: false,
            packageId: "Ludeon.RimWorld.Biotech",
            defName: "Baseliner",
            xenotypeName: null);
        Assert.Equal("ludeon.rimworld.biotech/Baseliner", namedRevertKey);

        var customRevertKey = VreLycanthropeRules.MorphBreakdownKey(
            nowMorphed: false,
            packageId: "Ludeon.RimWorld.Biotech",
            defName: "Baseliner",
            xenotypeName: "Custom Pack Leader");
        Assert.Null(customRevertKey);

        const string metricId = "mod.vanillaracesexpanded.lycanthrope.morphs.completed";
        var state = EmptyState();
        OptionalTransitionRecording.RecordCount(state, "completed", 1, morphKey, withBreakdown: true);
        OptionalTransitionRecording.RecordCount(state, "completed", 1, customRevertKey, withBreakdown: true);

        var metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(state, 100, metrics),
            "completed", metricId, withBreakdown: true);

        var entry = metrics[metricId];
        Assert.Equal(2L, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, entry.Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, entry.Values["vanillaracesexpanded.lycanthrope/VRE_Wolfman"].IntegerValue);
        Assert.False(entry.Values.ContainsKey("ludeon.rimworld.biotech/Baseliner"));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void VreLycanthropeReimplantRequiresLiveRecipientPlayerSideAndAppliedChange(
        bool liveRecipient, bool playerInvolved, bool genesOrComaApplied, bool expected)
    {
        Assert.Equal(expected, VreLycanthropeRules.ReimplantCommitted(
            liveRecipient, playerInvolved, genesOrComaApplied));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void VreFungoidConversionRequiresFullSeverityRemovalAndGeneInfectedHediff(
        bool severityReached,
        bool liveColonyAffectedPawn,
        bool infectionHediffRemoved,
        bool infectedHediffPresent,
        bool expected)
    {
        Assert.Equal(expected, VreFungoidRules.ConversionCommitted(
            severityReached, liveColonyAffectedPawn, infectionHediffRemoved, infectedHediffPresent));
    }

    [Theory]
    [InlineData(true, true, true, true, true, true, 0, 2, true, nameof(MetapodOutcome.EvolutionOrChange))]
    [InlineData(true, true, true, true, true, true, 2, 2, true, nameof(MetapodOutcome.EvolutionOrChange))]
    [InlineData(true, true, true, true, true, false, 2, 2, true, nameof(MetapodOutcome.EvolutionOrChange))]
    [InlineData(true, true, true, true, true, false, 2, 2, false, nameof(MetapodOutcome.None))]
    [InlineData(true, true, true, false, true, true, 0, 2, true, nameof(MetapodOutcome.None))]
    [InlineData(true, true, true, true, false, true, 0, 2, true, nameof(MetapodOutcome.None))]
    [InlineData(true, true, false, true, false, false, 3, 0, true, nameof(MetapodOutcome.Reversion))]
    [InlineData(true, true, false, true, false, true, 2, 0, true, nameof(MetapodOutcome.Reversion))]
    [InlineData(true, true, false, true, false, false, 0, 0, false, nameof(MetapodOutcome.None))]
    [InlineData(true, true, false, true, false, false, 2, 1, true, nameof(MetapodOutcome.None))]
    [InlineData(false, true, true, true, true, true, 0, 2, true, nameof(MetapodOutcome.None))]
    [InlineData(true, false, true, true, true, true, 0, 2, true, nameof(MetapodOutcome.None))]
    public void VreInsectorClassifiesEvolutionsChangesAndReversionsFromMetapodCompletion(
        bool livePlayerPawn,
        bool metapodRemoved,
        bool hasTargetGeneline,
        bool hasEvolutionGeneAfter,
        bool targetGenelineMatchedAfter,
        bool genelineChanged,
        int beforeGenelineGeneCount,
        int afterGenelineGeneCount,
        bool genelineGenesChanged,
        string expectedOutcome)
    {
        var actual = VreInsectorRules.ClassifyCompletion(
            livePlayerPawn,
            metapodRemoved,
            hasTargetGeneline,
            hasEvolutionGeneAfter,
            targetGenelineMatchedAfter,
            genelineChanged,
            beforeGenelineGeneCount,
            afterGenelineGeneCount,
            genelineGenesChanged);
        Assert.Equal(expectedOutcome, actual.ToString());
    }

    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(false, true, true, true, true, false)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, true, true, false, false)]
    public void AlphaAnimalsMetamorphosisRequiresDestroyedPlayerSourceAndValidCocoon(
        bool sourceSpawnedAndPlayer,
        bool spawnedValid,
        bool sameMap,
        bool parentDestroyed,
        bool expectedCocoonDef,
        bool expected)
    {
        Assert.Equal(expected, AlphaAnimalsRules.MetamorphosisCommitted(
            sourceSpawnedAndPlayer, spawnedValid, sameMap, parentDestroyed, expectedCocoonDef));
    }

    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(false, true, true, true, true, false)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, true, true, false, false)]
    public void AlphaAnimalsCocoonHatchRequiresDestroyedPlayerCocoonAndLivePlayerInsectoid(
        bool playerCocoonBefore,
        bool cocoonDestroyed,
        bool livePawnSpawned,
        bool playerPawnSpawned,
        bool sameMap,
        bool expected)
    {
        Assert.Equal(expected, AlphaAnimalsRules.CocoonHatchCommitted(
            playerCocoonBefore, cocoonDestroyed, livePawnSpawned, playerPawnSpawned, sameMap));
    }

    [Theory]
    [InlineData(0.25f, true, true, 0.25d)]
    [InlineData(12.5f, true, true, 12.5d)]
    [InlineData(0f, true, true, 0d)]
    [InlineData(-1f, true, true, 0d)]
    [InlineData(float.NaN, true, true, 0d)]
    [InlineData(float.PositiveInfinity, true, true, 0d)]
    [InlineData(1f, false, true, 0d)]
    [InlineData(1f, true, false, 0d)]
    public void VreSanguophageOnlyAcceptsPositiveFinitePlayerHemogenPipeUnits(
        float stored, bool matchingPipe, bool playerOwned, double expected)
    {
        Assert.Equal(expected, VreSanguophageRules.AcceptedUnits(stored, matchingPipe, playerOwned), 6);
    }
}
