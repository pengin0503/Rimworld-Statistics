using RimworldStatistics.VfeSpacer;
using RimworldStatistics.VweNonLethal;
using RimworldStatistics.AlphaBiomes;
using RimworldStatistics.VleLandmarks;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch2SpacerNonLethalBiomesLandmarksTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
    private const string Campaign = "cafe1234cafe1234cafe1234cafe1234";

    private static ColonyIdentity TestIdentity() => new()
    {
        SeedString = "batch2-spacer-nonlethal-biomes-landmarks",
        PersistentRandomValue = 23,
        StartingTile = "8,11",
        GameStartAbsTick = 0,
        FirstWealthBits = 12,
        StartingPawnIds = { "Thing_Human1" },
        ModCampaignId = Campaign,
    };

    [Theory]
    [InlineData(50, 51, 100, 1, false)]
    [InlineData(99, 100, 100, 1, true)]
    [InlineData(99, 105, 100, 1, true)]
    [InlineData(100, 100, 100, 0, false)]
    [InlineData(50, 50, 100, 0, false)]
    [InlineData(50, 40, 100, 0, false)]
    [InlineData(-1, 5, 100, 0, false)]
    [InlineData(50, 60, 0, 0, false)]
    public void VfeSpacerEvaluateRepairComputesClampedHpAndCompletionOnlyOnFullRestoration(
        int beforeHp,
        int afterHp,
        int maxHp,
        int expectedHp,
        bool expectedCompleted)
    {
        var outcome = VfeSpacerRules.EvaluateRepair(beforeHp, afterHp, maxHp);
        Assert.Equal(expectedHp, outcome.HpRestored);
        Assert.Equal(expectedCompleted, outcome.CompletedItem);
    }

    [Theory]
    [InlineData("VanillaExpanded.VWENL", "VWE_Gun_Taser", true, "VanillaExpanded.VWENL", "VWE_ConditionalStun", null, null, true, "vanillaexpanded.vwenl/VWE_Gun_Taser")]
    [InlineData("VanillaExpanded.VWENL", "VWE_Gun_Taser", false, "VanillaExpanded.VWENL", "VWE_ConditionalStun", null, null, true, null)]
    [InlineData("Ludeon.RimWorld", "Gun_Autopistol", true, "VanillaExpanded.VWENL", "VWENL_TearGas", null, null, true, "vanillaexpanded.vwenl/VWENL_TearGas")]
    [InlineData(null, null, false, null, null, "VanillaExpanded.VWENL", "VWE_Anesthetic", true, "vanillaexpanded.vwenl/VWE_Anesthetic")]
    [InlineData(null, null, false, null, null, "VanillaExpanded.VWENL", "VWE_TearGas", false, null)]
    [InlineData(null, null, false, null, null, "Ludeon.RimWorld", "ToxicBuildup", true, null)]
    public void VweNonLethalCauseResolutionRequiresPlayerOrActiveMapOwnership(
        string? weaponPkg,
        string? weaponDef,
        bool instigatorIsPlayer,
        string? dmgPkg,
        string? dmgDef,
        string? hediffPkg,
        string? hediffDef,
        bool onPlayerPresentMap,
        string? expectedKey)
    {
        var actual = VweNonLethalRules.ResolveCauseDef(
            weaponPkg, weaponDef, instigatorIsPlayer,
            dmgPkg, dmgDef, hediffPkg, hediffDef, onPlayerPresentMap);
        Assert.Equal(expectedKey, actual);
    }

    [Theory]
    [InlineData(false, true, false, true, "vanillaexpanded.vwenl/VWE_Gun_Taser", true)]
    [InlineData(true, true, false, true, "vanillaexpanded.vwenl/VWE_Gun_Taser", false)]
    [InlineData(false, false, false, true, "vanillaexpanded.vwenl/VWE_Gun_Taser", false)]
    [InlineData(false, true, true, true, "vanillaexpanded.vwenl/VWE_Gun_Taser", false)]
    [InlineData(false, true, false, false, "vanillaexpanded.vwenl/VWE_Gun_Taser", false)]
    [InlineData(false, true, false, true, null, false)]
    public void VweNonLethalCommittedDownRejectsLethalAlreadyDownedAndNonHostile(
        bool wasDowned,
        bool isDowned,
        bool isDead,
        bool isHostile,
        string? causeKey,
        bool expected)
    {
        Assert.Equal(expected, VweNonLethalRules.IsCommittedDown(
            wasDowned, isDowned, isDead, isHostile, causeKey));
    }

    [Fact]
    public void AlphaBiomesHomeBiomesDeduplicateMapsFilterOwnedBiomesAndTrackZeroMatches()
    {
        var entry = AlphaBiomesRules.HomeBiomes([
            (1, "sarg.alphabiomes", "AB_GallatrossGraveyard"),
            (1, "sarg.alphabiomes", "AB_GallatrossGraveyard"),
            (2, "Ludeon.RimWorld", "TemperateForest"),
            (3, "sarg.alphabiomes", "AB_MycoticJungle"),
        ])!;

        Assert.Equal(2, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1, entry.Values["sarg.alphabiomes/AB_GallatrossGraveyard"].IntegerValue);
        Assert.Equal(1, entry.Values["sarg.alphabiomes/AB_MycoticJungle"].IntegerValue);
        Assert.Equal(MetricCoverage.SnapshotOnly, entry.Values[MetricEntry.Scalar].Coverage);
        Assert.False(entry.Values.ContainsKey("ludeon.rimworld/TemperateForest"));

        var none = AlphaBiomesRules.HomeBiomes([(10, "Ludeon.RimWorld", "TemperateForest")])!;
        Assert.Equal(0, none.Values[MetricEntry.Scalar].IntegerValue);

        Assert.Null(AlphaBiomesRules.HomeBiomes([(99, null, null)]));
    }

    [Fact]
    public void VleLandmarksDeduplicatePerMapAndHandleZeroMatchesAndMissingTiles()
    {
        var entry = VleLandmarksRules.HomeLandmarks([
            (1, ["VEE_MeteorCrater", "VEE_MeteorCrater", "VEE_AnimalHabitat"]),
            (1, ["VEE_MeteorCrater"]),
            (2, []),
            (3, ["VEE_MeteorCrater"]),
        ])!;

        Assert.Equal(2, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2, entry.Values["vanillaexpanded.vexploratione/VEE_MeteorCrater"].IntegerValue);
        Assert.Equal(1, entry.Values["vanillaexpanded.vexploratione/VEE_AnimalHabitat"].IntegerValue);
        Assert.Equal(MetricCoverage.SnapshotOnly, entry.Values[MetricEntry.Scalar].Coverage);

        var partial = VleLandmarksRules.HomeLandmarks([
            (1, ["VEE_MeteorCrater"]),
            (2, null),
        ])!;
        Assert.Equal(MetricCoverage.Partial, partial.Values[MetricEntry.Scalar].Coverage);
        Assert.Null(VleLandmarksRules.HomeLandmarks([(1, null)]));
    }

    [Fact]
    public void Batch2CheckpointsAccumulateBranchAndReloadCleanly()
    {
        var spacerUnit = "mod.vanillaexpanded.vfespacer.repair_shelf";
        var spacerHpKey = spacerUnit + ".hp_repaired";
        var spacerItemsKey = spacerUnit + ".items_repaired";
        var nonLethalUnit = "mod.vanillaexpanded.vwenl.nonlethal_downs";
        var nonLethalKey = nonLethalUnit + ".completed";
        var drillUnit = "mod.sarg.alphabiomes.core_drill";
        var drillKey = drillUnit + ".completed";

        var families = new LiveFamily[]
        {
            new()
            {
                Name = spacerUnit,
                Revision = 1,
                Build = output =>
                {
                    output.PutCount("hp_repaired", spacerHpKey);
                    OptionalTransitionRecording.PutCount(output, "items_repaired", spacerItemsKey, withBreakdown: true);
                },
            },
            new()
            {
                Name = nonLethalUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", nonLethalKey, withBreakdown: true),
            },
            new()
            {
                Name = drillUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", drillKey, withBreakdown: true),
            },
        };

        var acc = LiveMetricAccumulator.StartNew(families.Select(f => f.Spec).ToArray(), 0);
        acc.Family(spacerUnit)!.Count("hp_repaired", 5);
        OptionalTransitionRecording.RecordCount(acc.Family(spacerUnit)!, "items_repaired", 1,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "Apparel_PowerArmor"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(nonLethalUnit)!, "completed", 1,
            OptionalTransitionRecording.QualifiedDefKey("VanillaExpanded.VWENL", "VWE_Gun_Taser"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(drillUnit)!, "completed", 2,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "ChunkMarble"), true);

        var save1 = acc.PrepareSave(100, T0, "save1")!;
        acc.Commit(save1);

        acc.Family(spacerUnit)!.Count("hp_repaired", 3);
        OptionalTransitionRecording.RecordCount(acc.Family(nonLethalUnit)!, "completed", 1,
            OptionalTransitionRecording.QualifiedDefKey("VanillaExpanded.VWENL", "VWE_Anesthetic"), true);
        var branchA = acc.PrepareSave(200, T0.AddMinutes(5), "branchA")!;
        acc.Commit(branchA);

        var reloadedWithoutMods = LiveMetricAccumulator.Resume(save1.Json, Array.Empty<FamilySpec>(), 100);
        var branchB = reloadedWithoutMods.PrepareSave(250, T0.AddMinutes(10), "branchB")!;
        reloadedWithoutMods.Commit(branchB);

        var obsA = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchA.Json).Payload!, branchA.Json,
            TestIdentity(), "1.7.4985", null, families);
        var obsB = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchB.Json).Payload!, branchB.Json,
            TestIdentity(), "1.7.4985", null, families);

        Assert.Equal(8L, obsA.Scalar(spacerHpKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsA.Scalar(spacerItemsKey, 1)!.IntegerValue);
        Assert.Equal(2L, obsA.Scalar(nonLethalKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsA.Metrics[nonLethalKey].Values["vanillaexpanded.vwenl/VWE_Gun_Taser"].IntegerValue);
        Assert.Equal(1L, obsA.Metrics[nonLethalKey].Values["vanillaexpanded.vwenl/VWE_Anesthetic"].IntegerValue);
        Assert.Equal(2L, obsA.Scalar(drillKey, 1)!.IntegerValue);

        Assert.Equal(5L, obsB.Scalar(spacerHpKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsB.Scalar(nonLethalKey, 1)!.IntegerValue);
        Assert.Equal(2L, obsB.Scalar(drillKey, 1)!.IntegerValue);
    }
}
