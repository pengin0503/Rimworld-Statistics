using RimworldStatistics.AnomaliesExpected;
using RimworldStatistics.BiomesBees;
using RimworldStatistics.BiomesCaverns;
using RimworldStatistics.VqeDeadlife;
using RimworldStatistics.WayBetterRomance;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch4BiomesDeadlifeAnomalyRomanceTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
    private const string Campaign = "bead5678bead5678bead5678bead5678";

    private static ColonyIdentity TestIdentity() => new()
    {
        SeedString = "batch4-biomes-deadlife-anomaly-romance",
        PersistentRandomValue = 41,
        StartingTile = "14,22",
        GameStartAbsTick = 0,
        FirstWealthBits = 18,
        StartingPawnIds = { "Thing_Human1" },
        ModCampaignId = Campaign,
    };

    [Theory]
    [InlineData(true, 0.2f, 0.3f, "Ludeon.RimWorld", "Plant_Rice", "ludeon.rimworld/Plant_Rice")]
    [InlineData(true, 0.95f, 1.0f, "BiomesTeam.BiomesBees", "BMT_Clover", "biomesteam.biomesbees/BMT_Clover")]
    [InlineData(true, 1.0f, 1.0f, "Ludeon.RimWorld", "Plant_Rice", null)]
    [InlineData(true, 0.5f, 0.5f, "Ludeon.RimWorld", "Plant_Rice", null)]
    [InlineData(true, 0.5f, 0.4f, "Ludeon.RimWorld", "Plant_Rice", null)]
    [InlineData(false, 0.2f, 0.3f, "Ludeon.RimWorld", "Plant_Rice", null)]
    [InlineData(true, float.NaN, 0.3f, "Ludeon.RimWorld", "Plant_Rice", null)]
    public void BiomesBeesPollinationRequiresGrowthGainAndPlayerRelevance(
        bool playerRelevant,
        float beforeGrowth,
        float afterGrowth,
        string? pkg,
        string? def,
        string? expected)
    {
        Assert.Equal(expected, BiomesBeesRules.ResolvePollinationBreakdown(
            playerRelevant, beforeGrowth, afterGrowth, pkg, def));
    }

    [Fact]
    public void BiomesCavernsHomeBiomesDeduplicateMapsAndHandleMissingTiles()
    {
        var entry = BiomesCavernsRules.HomeBiomes([
            (1, "BiomesTeam.BiomesCaverns", "BMT_CrystalCaverns"),
            (1, "BiomesTeam.BiomesCaverns", "BMT_CrystalCaverns"),
            (2, "Ludeon.RimWorld", "TemperateForest"),
            (3, "BiomesTeam.BiomesCaverns", "BMT_FungalForest"),
        ])!;

        Assert.Equal(2, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1, entry.Values["biomesteam.biomescaverns/BMT_CrystalCaverns"].IntegerValue);
        Assert.Equal(1, entry.Values["biomesteam.biomescaverns/BMT_FungalForest"].IntegerValue);
        Assert.Equal(MetricCoverage.SnapshotOnly, entry.Values[MetricEntry.Scalar].Coverage);
        Assert.False(entry.Values.ContainsKey("ludeon.rimworld/TemperateForest"));

        var none = BiomesCavernsRules.HomeBiomes([(10, "Ludeon.RimWorld", "TemperateForest")])!;
        Assert.Equal(0, none.Values[MetricEntry.Scalar].IntegerValue);

        var partial = BiomesCavernsRules.HomeBiomes([
            (1, "BiomesTeam.BiomesCaverns", "BMT_EarthenDepths"),
            (2, null, null),
        ])!;
        Assert.Equal(MetricCoverage.Partial, partial.Values[MetricEntry.Scalar].Coverage);

        Assert.Null(BiomesCavernsRules.HomeBiomes([(99, null, null)]));
    }

    [Theory]
    [InlineData(true, true, "vanillaquestsexpanded.deadlife", "VQED_AncientPlanningDesk", "vanillaquestsexpanded.deadlife/VQED_AncientPlanningDesk")]
    [InlineData(false, true, "vanillaquestsexpanded.deadlife", "VQED_AncientPlanningDesk", null)]
    [InlineData(true, false, "vanillaquestsexpanded.deadlife", "VQED_AncientPlanningDesk", null)]
    public void VqeDeadlifeSiloDecryptionRequiresPlayerAndValidBuilding(
        bool actorIsPlayer,
        bool buildingValid,
        string pkg,
        string def,
        string? expected)
    {
        Assert.Equal(expected, VqeDeadlifeRules.ResolveSiloStudyBreakdown(
            actorIsPlayer, buildingValid, pkg, def));
    }

    [Theory]
    [InlineData(true, true, false, true, "vanillaquestsexpanded.deadlife", "VQED_AncientBurnoutGenerator", "vanillaquestsexpanded.deadlife/VQED_AncientBurnoutGenerator")]
    [InlineData(true, true, true, true, "vanillaquestsexpanded.deadlife", "VQED_AncientBurnoutGenerator", null)]
    [InlineData(true, true, false, false, "vanillaquestsexpanded.deadlife", "VQED_AncientBurnoutGenerator", null)]
    [InlineData(false, true, false, true, "vanillaquestsexpanded.deadlife", "VQED_AncientBurnoutGenerator", null)]
    [InlineData(true, false, false, true, "vanillaquestsexpanded.deadlife", "VQED_AncientBurnoutGenerator", null)]
    public void VqeDeadlifePowerKickstartRequiresFalseToTrueTransition(
        bool actorIsPlayer,
        bool buildingValid,
        bool wasActive,
        bool isActiveAfter,
        string pkg,
        string def,
        string? expected)
    {
        Assert.Equal(expected, VqeDeadlifeRules.ResolvePowerKickstartBreakdown(
            actorIsPlayer, buildingValid, wasActive, isActiveAfter, pkg, def));
    }

    [Fact]
    public void AnomaliesExpectedStudiedEntitiesFilterRootOwnedEntriesAndDeduplicate()
    {
        var entry = AnomaliesExpectedRules.StudiedEntities([
            new(false, "MrHydralisk.AnomaliesExpected", "AE_Codex_MeatGrinder", null, null, 2),
            new(false, "MrHydralisk.AnomaliesExpected", "AE_Codex_MeatGrinder", null, null, 3),
            new(true, null, null, "MrHydralisk.AnomaliesExpected", "AE_SnowSoldier", 1),
            new(false, null, null, "MrHydralisk.AnomaliesExpected", "AE_BloodPuddle", 1),
            new(false, "MrHydralisk.AnomaliesExpected", "AE_Codex_Unstudied", null, null, 0),
            new(false, "Ludeon.RimWorld.Anomaly", "Revenant", null, null, 3),
        ])!;

        Assert.Equal(2L, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, entry.Values["mrhydralisk.anomaliesexpected/AE_Codex_MeatGrinder"].IntegerValue);
        Assert.Equal(1L, entry.Values["mrhydralisk.anomaliesexpected/AE_BloodPuddle"].IntegerValue);
        Assert.False(entry.Values.ContainsKey("mrhydralisk.anomaliesexpected/AE_SnowSoldier"));
        Assert.False(entry.Values.ContainsKey("mrhydralisk.anomaliesexpected/AE_Codex_Unstudied"));
        Assert.False(entry.Values.ContainsKey("ludeon.rimworld.anomaly/Revenant"));

        var noneUnlocked = AnomaliesExpectedRules.StudiedEntities([
            new(false, "MrHydralisk.AnomaliesExpected", "AE_Codex_Unstudied", null, null, 0),
        ])!;
        Assert.Equal(0L, noneUnlocked.Values[MetricEntry.Scalar].IntegerValue);

        Assert.Null(AnomaliesExpectedRules.StudiedEntities([
            new(false, "Ludeon.RimWorld.Anomaly", "Revenant", null, null, 2),
        ]));
    }

    [Theory]
    [InlineData(true, true, true, true, WayBetterRomanceRules.DateBreakdownKey)]
    [InlineData(true, true, true, false, WayBetterRomanceRules.HangoutBreakdownKey)]
    [InlineData(false, true, true, true, null)]
    [InlineData(true, false, true, true, null)]
    [InlineData(true, true, false, true, null)]
    public void WayBetterRomanceDateCompletionGuardsParticipantsAndFollowState(
        bool playerInvolved,
        bool participantsAlive,
        bool partnerWasFollowingExpectedJob,
        bool isDate,
        string? expected)
    {
        Assert.Equal(expected, WayBetterRomanceRules.ResolveDateBreakdown(
            playerInvolved, participantsAlive, partnerWasFollowingExpectedJob, isDate));
    }

    [Fact]
    public void Batch4CheckpointsAccumulateBranchAndReloadCleanlyWithoutMods()
    {
        var beesUnit = "mod.biomesteam.biomesbees.pollinations";
        var beesKey = beesUnit + ".completed";
        var siloUnit = "mod.vanillaquestsexpanded.deadlife.silo_decryptions";
        var siloKey = siloUnit + ".completed";
        var powerUnit = "mod.vanillaquestsexpanded.deadlife.power_kickstarts";
        var powerKey = powerUnit + ".completed";
        var datesUnit = "mod.divinederivative.romance.dates";
        var datesKey = datesUnit + ".completed";

        var families = new LiveFamily[]
        {
            new()
            {
                Name = beesUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", beesKey, withBreakdown: true),
            },
            new()
            {
                Name = siloUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", siloKey, withBreakdown: true),
            },
            new()
            {
                Name = powerUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", powerKey, withBreakdown: true),
            },
            new()
            {
                Name = datesUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", datesKey, withBreakdown: true),
            },
        };

        var acc = LiveMetricAccumulator.StartNew(families.Select(f => f.Spec).ToArray(), 0);
        OptionalTransitionRecording.RecordCount(acc.Family(beesUnit)!, "completed", 1,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "Plant_Rice"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(siloUnit)!, "completed", 1,
            OptionalTransitionRecording.QualifiedDefKey("vanillaquestsexpanded.deadlife", "VQED_AncientPlanningDesk"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(powerUnit)!, "completed", 1,
            OptionalTransitionRecording.QualifiedDefKey("vanillaquestsexpanded.deadlife", "VQED_AncientBurnoutGenerator"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(datesUnit)!, "completed", 1,
            WayBetterRomanceRules.DateBreakdownKey, true);

        var save1 = acc.PrepareSave(100, T0, "save1")!;
        acc.Commit(save1);

        OptionalTransitionRecording.RecordCount(acc.Family(beesUnit)!, "completed", 2,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "Plant_Rice"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(datesUnit)!, "completed", 1,
            WayBetterRomanceRules.HangoutBreakdownKey, true);
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

        Assert.Equal(3L, obsA.Scalar(beesKey, 1)!.IntegerValue);
        Assert.Equal(3L, obsA.Metrics[beesKey].Values["ludeon.rimworld/Plant_Rice"].IntegerValue);
        Assert.Equal(1L, obsA.Scalar(siloKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsA.Scalar(powerKey, 1)!.IntegerValue);
        Assert.Equal(2L, obsA.Scalar(datesKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsA.Metrics[datesKey].Values[WayBetterRomanceRules.DateBreakdownKey].IntegerValue);
        Assert.Equal(1L, obsA.Metrics[datesKey].Values[WayBetterRomanceRules.HangoutBreakdownKey].IntegerValue);

        Assert.Equal(1L, obsB.Scalar(beesKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsB.Scalar(siloKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsB.Scalar(powerKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsB.Scalar(datesKey, 1)!.IntegerValue);
    }
}
