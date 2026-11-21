using System.Text.Json.Nodes;
using Xunit;

namespace RimworldStatistics.Core.Tests;

public sealed class ColonyWorkAttributionTests
{
    [Theory]
    [InlineData(true, false, false, true)]   // Free colonist / colony slave / colony mech / colony animal
    [InlineData(true, false, true, true)]    // Player-faction non-prisoner with player host faction
    [InlineData(false, true, true, true)]    // Colony prisoner performing labor (e.g. Prison Labor)
    [InlineData(true, true, true, true)]     // Imprisoned own colonist held in colony custody
    [InlineData(true, true, false, false)]   // Captured colonist held prisoner by a hostile faction
    [InlineData(false, true, false, false)]  // Non-player prisoner held by another faction
    [InlineData(false, false, true, false)]  // Visiting allied guest (HostFaction == player, GuestStatus != Prisoner)
    [InlineData(false, false, false, false)] // Raider / trader / wild pawn
    public void IsColonyWorker_ClassifiesPrisonerGuestAndPlayerLaborWithoutOverlap(
        bool isPlayerFaction,
        bool isPrisoner,
        bool isHostPlayerFaction,
        bool expected)
    {
        Assert.Equal(
            expected,
            ColonyWorkAttribution.IsColonyWorker(isPlayerFaction, isPrisoner, isHostPlayerFaction));
    }

    // ----- #392: totals continued from 1.9, when only player-faction pawns counted -----

    private static readonly DateTime T0 = new(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly FamilySpec[] Specs =
    [
        WorkFamily.Family.Spec, WorkDetailsFamily.Family.Spec, ChoreFamily.Family.Spec, FoodFamily.Family.Spec,
        FishingFamily.Family.Spec,
    ];

    [Theory]
    [InlineData(true, false)]    // a player-faction pawn (or a colonist the colony itself holds): counted before 2.0 too
    [InlineData(false, true)]    // a prisoner in the colony's custody: counted only under the colony-worker rule
    public void OnlyAPrisonerOutsideThePlayersFactionIsNewToTheRule(bool isPlayerFaction, bool onlyUnderThisRule)
        => Assert.Equal(onlyUnderThisRule, ColonyWorkAttribution.CountsOnlyUnderThisRule(isPlayerFaction));

    /// <summary>The checkpoint as 1.9 wrote it: the same families and revisions, without the colony-worker rule marker.</summary>
    private static string AsWrittenBy19(string json)
    {
        var root = JsonNode.Parse(json)!;
        foreach (var (_, family) in root["families"]!.AsObject())
            Assert.True(family!["metrics"]!.AsObject().Remove(ColonyWorkAttribution.RuleMarker));
        return root.ToJsonString();
    }

    private static MetricObservation Save(LiveMetricAccumulator acc, int tick, string id)
    {
        var draft = acc.PrepareSave(tick, T0, id)!;
        acc.Commit(draft);
        return CheckpointMetrics.ToObservation(draft.Payload!, draft.Json, new ColonyIdentity { ModCampaignId = "work-rule" }, null,
            [AcquisitionUnits.Odyssey]);
    }

    private static MetricValue Scalar(MetricObservation obs, string key) => obs.Metrics[key].Values[MetricEntry.Scalar];

    private static void Work(LiveMetricAccumulator acc, bool prisoner, int tick)
    {
        void Do(string family, Action<FamilyState> record)
        {
            var s = acc.Family(family)!;
            record(s);
            if (prisoner)
                ColonyWorkAttribution.CountedPrisonerWork(s);
        }
        var worker = prisoner ? "Thing_Prisoner1" : "Thing_Colonist1";
        Do(WorkFamily.Name, s => WorkFamily.Harvested(s, worker, "Thing_Plant" + tick, "Plant_Rice", 10, tick));
        Do(WorkDetailsFamily.Name, s => WorkDetailsFamily.MinedYield(s, "Steel", 20));
        Do(ChoreFamily.Name, ChoreFamily.FilthCleared);
        Do(FoodFamily.Name, s => FoodFamily.ZoneHarvested(s, 10, tick));
        Do(FishingFamily.Name, s => FishingFamily.RecordAccepted(s, "job" + tick, "catch",
            new Dictionary<string, int> { ["Fish_Tuna"] = 2 }, tick));
    }

    private static readonly string[] WorkKeys =
    [
        WorkFamily.HarvestUnits, WorkFamily.BestHarvester, WorkDetailsFamily.MinedByDef, ChoreFamily.FilthRemoved,
        FoodFamily.HarvestPerGrowingCell, FishingFamily.SuccessfulActions, FishingFamily.AcceptedUnitsByDef,
    ];

    private static LiveMetricAccumulator StartNew()
    {
        var acc = LiveMetricAccumulator.StartNew(Specs, 0);
        FoodFamily.GrowingCellsBaseline(acc.Family(FoodFamily.Name)!, 100, 0);
        return acc;
    }

    [Fact]
    public void TotalsBegunUnderTheRule_StayCompleteWithPrisonersWork()
    {
        var acc = StartNew();
        foreach (var spec in Specs)
            Assert.NotNull(acc.Family(spec.Name)!.Peek(ColonyWorkAttribution.RuleMarker));

        Work(acc, prisoner: false, 100);
        Work(acc, prisoner: true, 200);
        var obs = Save(acc, 300, "fresh");

        Assert.All(WorkKeys, key => Assert.Equal(MetricCoverage.Complete, Scalar(obs, key).Coverage));
        Assert.Equal(20L, Scalar(obs, WorkFamily.HarvestUnits).IntegerValue);
        Assert.All(Specs, spec => Assert.False(ColonyWorkAttribution.MixesRules(acc.Family(spec.Name)!)));
    }

    [Fact]
    public void TotalsContinuedFrom19_AreALowerBoundOnceAPrisonersWorkIsCounted()
    {
        var old = StartNew();
        Work(old, prisoner: false, 100);
        var draft = old.PrepareSave(1_000, T0, "v19")!;
        var json19 = AsWrittenBy19(draft.Json);

        // Loaded in 2.0: the same revision continues without the marker. Player pawns' work alone leaves it complete.
        var acc = LiveMetricAccumulator.Resume(json19, Specs, 1_000);
        foreach (var spec in Specs)
            Assert.Null(acc.Family(spec.Name)!.Peek(ColonyWorkAttribution.RuleMarker));
        Work(acc, prisoner: false, 1_100);
        var unmixed = Save(acc, 1_200, "v20-a");
        Assert.All(WorkKeys, key => Assert.Equal(MetricCoverage.Complete, Scalar(unmixed, key).Coverage));

        // A prisoner's work: the earlier interval counted none, so the colony workers' totals are only a lower bound.
        Work(acc, prisoner: true, 1_300);
        var mixed = Save(acc, 1_400, "v20-b");
        Assert.All(WorkKeys, key =>
        {
            var v = Scalar(mixed, key);
            Assert.Equal(MetricCoverage.Partial, v.Coverage);
            Assert.True(v.Weakened);
        });
        Assert.Equal(30L, Scalar(mixed, WorkFamily.HarvestUnits).IntegerValue);   // still counted: the value is what was seen
        // What is not a colony worker's work keeps its coverage.
        Assert.Equal(MetricCoverage.Complete, Scalar(mixed, WorkFamily.Constructions).Coverage);
        Assert.Equal(MetricCoverage.Complete, Scalar(mixed, ChoreFamily.MealIngestions).Coverage);
        Assert.Equal(MetricCoverage.Complete, Scalar(mixed, FoodFamily.Nutrition).Coverage);

        // The mark is part of the totals: it survives a save and load, and a later load never makes them complete again.
        var reloaded = LiveMetricAccumulator.Resume(acc.LoadedJson, Specs, 1_400);
        Assert.All(Specs, spec => Assert.True(ColonyWorkAttribution.MixesRules(reloaded.Family(spec.Name)!)));
        Assert.Equal(MetricCoverage.Partial, Scalar(Save(reloaded, 1_500, "v20-c"), WorkFamily.HarvestUnits).Coverage);
    }

    [Fact]
    public void TheRuleMarker_IsNotARecording_ForStartupBaselines()
    {
        var acc = StartNew();
        var work = acc.Family(WorkFamily.Name)!;
        Assert.True(work.IsFreshAt(0));
        Assert.False(work.IsFreshAt(1));
        Work(acc, prisoner: false, 100);
        Assert.False(work.IsFreshAt(0));
        Assert.True(LiveMetricAccumulator.StartNew([MembershipFamily.Family.Spec], 50).Family(MembershipFamily.Name)!.IsFreshAt(50));
    }

    [Fact]
    public void TotalsThatStartOver_BeginUnderTheRule()
    {
        // A family recorded by an older revision starts over, and a family first recorded later begins: both carry the marker.
        var acc = LiveMetricAccumulator.StartNew([new FamilySpec(WorkFamily.Name, WorkFamily.Family.Revision)], 0);
        acc.Family(WorkFamily.Name)!.Revision = 0;
        acc.Family(WorkFamily.Name)!.Metrics.Remove(ColonyWorkAttribution.RuleMarker);
        var draft = acc.PrepareSave(100, T0, "older-revision")!;

        var resumed = LiveMetricAccumulator.Resume(draft.Json, Specs, 100);
        Assert.All(Specs, spec => Assert.NotNull(resumed.Family(spec.Name)!.Peek(ColonyWorkAttribution.RuleMarker)));
        Assert.Equal(WorkFamily.Family.Revision, resumed.Family(WorkFamily.Name)!.Revision);
    }
}
