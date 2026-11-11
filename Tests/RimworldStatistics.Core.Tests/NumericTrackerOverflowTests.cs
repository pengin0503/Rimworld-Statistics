namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Every keyed collection of a family holds at most <see cref="FamilyOps.KeyCap"/> keys (numeric expansion design §4: "上限後は既存キーと
/// 独立総数のみ続行し、種類数・順位・追跡期間等の依存値は部分または不明"). These tests fill each family's active trackers to the cap
/// through the families' own methods and check that every output depending on a dropped key is partial, the "no sample" case
/// included (a dropped key may be the missing sample), while the outputs that do not depend on it stay complete.
/// </summary>
public sealed class NumericTrackerOverflowTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int Day = 60000;
    private static readonly int Cap = FamilyOps.KeyCap;

    private static (LiveMetricAccumulator Acc, FamilyState S) NewGame(LiveFamily family)
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { family.Spec }, 0);
        return (acc, acc.Family(family.Name)!);
    }

    private static MetricObservation Save(LiveMetricAccumulator acc, int tick)
    {
        var d = acc.PrepareSave(tick, T0, "S" + tick)!;
        acc.Commit(d);
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "c" }, null, null);
    }

    private static MetricValue V(MetricObservation o, string key) => o.Scalar(key, MetricKeys.Revision(key))!;

    private static void Partial(MetricObservation o, params string[] keys)
    {
        foreach (var key in keys)
            Assert.True(V(o, key).Coverage == MetricCoverage.Partial, key + " should be partial but is " + V(o, key).Coverage);
    }

    private static void Complete(MetricObservation o, params string[] keys)
    {
        foreach (var key in keys)
            Assert.True(V(o, key).Coverage == MetricCoverage.Complete, key + " should be complete but is " + V(o, key).Coverage);
    }

    /// <summary>Not weakened by the cap: complete, or not applicable when it has no sample of its own.</summary>
    private static void NotPartial(MetricObservation o, params string[] keys)
    {
        foreach (var key in keys)
            Assert.True(V(o, key).Coverage is MetricCoverage.Complete or MetricCoverage.NotApplicable,
                key + " should not be partial but is " + V(o, key).Coverage);
    }

    private static readonly IReadOnlyDictionary<string, int> NoSkills = new Dictionary<string, int>();

    // ----- lifetime (the Codex review finding) -----

    [Fact]
    public void Lifetime_JoinTrackerFull_TenureAndGainsArePartial_AndTheDroppedPawnGetsNoBaseline()
    {
        var (acc, s) = NewGame(LifetimeFamily.Family);
        LifetimeFamily.Baseline(s, Enumerable.Range(0, Cap).Select(i => "Thing_P" + i), 0, _ => NoSkills);
        LifetimeFamily.Joined(s, "Thing_X", Day, new Dictionary<string, int> { ["Shooting"] = 5 });   // dropped: no join tick
        LifetimeFamily.SkillChanged(s, "Thing_X", "Shooting", 5, 9);                                    // no baseline either
        LifetimeFamily.Left(s, "Thing_X", 3 * Day);
        var o = Save(acc, 4 * Day);
        Partial(o, LifetimeFamily.TenureMean, LifetimeFamily.JoinGainPeak);
        NotPartial(o, LifetimeFamily.FirstSkill20);
        Assert.Equal(0, V(o, LifetimeFamily.TenureMean).SampleCount);
        Assert.Null(V(o, LifetimeFamily.JoinGainPeak).IntegerValue);
    }

    [Fact]
    public void Lifetime_SkillBaselineFull_GainPeakIsPartial_TenureStaysComplete()
    {
        var (acc, s) = NewGame(LifetimeFamily.Family);
        LifetimeFamily.Joined(s, "Thing_A", 0, Enumerable.Range(0, Cap).ToDictionary(i => "Skill" + i, _ => 1));
        LifetimeFamily.Joined(s, "Thing_B", 0, new Dictionary<string, int> { ["Shooting"] = 5 });   // its baseline is dropped
        LifetimeFamily.SkillChanged(s, "Thing_B", "Shooting", 5, 12);
        LifetimeFamily.Left(s, "Thing_B", 2 * Day);
        var o = Save(acc, 4 * Day);
        Partial(o, LifetimeFamily.JoinGainPeak);
        NotPartial(o, LifetimeFamily.TenureMean);
        Assert.Equal(2.0, V(o, LifetimeFamily.TenureMean).RealValue);
    }

    [Fact]
    public void Lifetime_BelowTheCap_StaysComplete()
    {
        var (acc, s) = NewGame(LifetimeFamily.Family);
        LifetimeFamily.Joined(s, "Thing_B", 0, new Dictionary<string, int> { ["Shooting"] = 5 });
        LifetimeFamily.SkillChanged(s, "Thing_B", "Shooting", 5, 12);
        LifetimeFamily.Left(s, "Thing_B", 3 * Day);
        var o = Save(acc, 4 * Day);
        Complete(o, LifetimeFamily.JoinGainPeak, LifetimeFamily.TenureMean);
        Assert.Equal(7, V(o, LifetimeFamily.JoinGainPeak).IntegerValue);
    }

    // ----- research -----

    [Fact]
    public void Research_CompletedProjectsFull_DurationsArePartial()
    {
        var (acc, s) = NewGame(ResearchFamily.Family);
        for (var i = 0; i < Cap; i++)
        {
            ResearchFamily.Worked(s, "Project" + i, 2 * i);
            ResearchFamily.Finished(s, "Project" + i, 2 * i + 1);
        }
        ResearchFamily.Worked(s, "Dropped", Day);
        ResearchFamily.Finished(s, "Dropped", 2 * Day);

        var o = Save(acc, 3 * Day);
        Partial(o, ResearchFamily.Completed, ResearchFamily.DurationMin, ResearchFamily.DurationMax);
        NotPartial(o, ResearchFamily.FirstDay);
        Assert.Equal(Cap, V(o, ResearchFamily.Completed).IntegerValue);
    }

    [Fact]
    public void Research_StartTrackerFull_DurationsArePartial_CountStaysComplete()
    {
        var (acc, s) = NewGame(ResearchFamily.Family);
        for (var i = 0; i < Cap; i++)
            ResearchFamily.Worked(s, "Project" + i, 10);
        ResearchFamily.Worked(s, "Dropped", 10);
        ResearchFamily.Finished(s, "Dropped", 2 * Day);
        var o = Save(acc, 3 * Day);
        Partial(o, ResearchFamily.DurationMin, ResearchFamily.DurationMax);
        NotPartial(o, ResearchFamily.Completed, ResearchFamily.FirstDay);
        Assert.Equal(1, V(o, ResearchFamily.Completed).IntegerValue);
    }

    // ----- caravan -----

    [Fact]
    public void Caravan_ExpeditionTrackerFull_DurationDistanceAndLossesArePartial()
    {
        var (acc, s) = NewGame(CaravanFamily.Family);
        for (var i = 0; i < Cap; i++)
            CaravanFamily.Departed(s, "Caravan_" + i, 10);
        CaravanFamily.Departed(s, "Caravan_X", 20);
        CaravanFamily.Moved(s, "Caravan_X", "0:1", adjacent: true);
        CaravanFamily.Returned(s, "Caravan_X", Day);
        CaravanFamily.Departed(s, "Caravan_Y", 20);
        CaravanFamily.Lost(s, "Caravan_Y");
        var o = Save(acc, 2 * Day);
        Partial(o, CaravanFamily.LongestExpedition, CaravanFamily.LongestDistance, CaravanFamily.Lost_);
        NotPartial(o, CaravanFamily.Departures, CaravanFamily.TilesTravelled, CaravanFamily.UniqueTiles);
        Assert.Equal(Cap + 2, V(o, CaravanFamily.Departures).IntegerValue);
    }

    // ----- diplomacy and social: the same-tick de-duplication sets -----

    [Fact]
    public void Diplomacy_SameTickSetFull_NewChangesAreNotCounted_AndThePeaceCountsArePartial()
    {
        var (acc, s) = NewGame(DiplomacyFamily.Family);
        for (var i = 0; i < Cap; i++)
            DiplomacyFamily.RelationChanged(s, "Faction_" + i, DiplomacyFamily.Neutral, DiplomacyFamily.Ally, 100);
        // Both sides' notifications of one change: past the cap they cannot be told apart, so neither is counted.
        DiplomacyFamily.RelationChanged(s, "Faction_X", DiplomacyFamily.Hostile, DiplomacyFamily.Neutral, 100);
        DiplomacyFamily.RelationChanged(s, "Faction_X", DiplomacyFamily.Hostile, DiplomacyFamily.Neutral, 100);
        var o = Save(acc, Day);
        Assert.Equal(0, V(o, DiplomacyFamily.Reconciled).IntegerValue);
        Partial(o, DiplomacyFamily.Reconciled, DiplomacyFamily.AllyToHostile);
        NotPartial(o, DiplomacyFamily.AidRequests, DiplomacyFamily.GiftGoodwill);
    }

    [Fact]
    public void Diplomacy_TheNextTickStartsAnEmptySet_ButTheEarlierDropKeepsThemPartial()
    {
        var (acc, s) = NewGame(DiplomacyFamily.Family);
        for (var i = 0; i <= Cap; i++)
            DiplomacyFamily.RelationChanged(s, "Faction_" + i, DiplomacyFamily.Neutral, DiplomacyFamily.Ally, 100);
        DiplomacyFamily.RelationChanged(s, "Faction_X", DiplomacyFamily.Hostile, DiplomacyFamily.Neutral, 101);
        var o = Save(acc, Day);
        Assert.Equal(1, V(o, DiplomacyFamily.Reconciled).IntegerValue);
        Partial(o, DiplomacyFamily.Reconciled);
    }

    [Fact]
    public void Social_SameTickSetFull_NewPairsAreNotCounted_AndTheyArePartial()
    {
        var (acc, s) = NewGame(SocialFamily.Family);
        for (var i = 0; i < Cap; i++)
            SocialFamily.Relation(s, SocialFamily.Lover, "Thing_A" + i + "|Thing_B" + i, 100);
        SocialFamily.Relation(s, SocialFamily.Spouse, "Thing_C|Thing_D", 100);
        SocialFamily.Relation(s, SocialFamily.Spouse, "Thing_C|Thing_D", 100);
        var o = Save(acc, Day);
        Assert.Equal(0, V(o, SocialFamily.Marriages).IntegerValue);
        Assert.Equal(Cap, V(o, SocialFamily.Lovers).IntegerValue);
        Partial(o, SocialFamily.Marriages, SocialFamily.Lovers);
        NotPartial(o, SocialFamily.Fights, SocialFamily.Divorces);
    }

    // ----- disaster -----

    [Fact]
    public void Disaster_ActiveConditionsFull_ThatDefsCountAndDaysArePartial_OthersStayComplete()
    {
        var (acc, s) = NewGame(DisasterFamily.Family);
        for (var i = 0; i < Cap; i++)
            DisasterFamily.ConditionStarted(s, "SolarFlare", "GameCondition_" + i, 0, counted: true);
        DisasterFamily.ConditionStarted(s, "SolarFlare", "GameCondition_X", 0, counted: true);   // dropped
        DisasterFamily.ConditionStarted(s, "SolarFlare", "GameCondition_X", 10, counted: true);  // its duplicate: not counted again
        DisasterFamily.ConditionStarted(s, "ColdSnap", "GameCondition_C", 0, counted: true);
        DisasterFamily.ConditionEnded(s, "ColdSnap", "GameCondition_C", Day);
        var o = Save(acc, 2 * Day);
        Assert.Equal(Cap, V(o, DisasterFamily.SolarFlares).IntegerValue);
        Partial(o, DisasterFamily.SolarFlares, DisasterFamily.SolarFlareDays);
        NotPartial(o, DisasterFamily.ColdSnaps, DisasterFamily.ToxicFalloutDays, DisasterFamily.HeatWaves);
        Assert.Equal(1, V(o, DisasterFamily.ColdSnaps).IntegerValue);
    }

    // ----- medicine -----

    [Fact]
    public void Medicine_DiseaseTrackerFull_DurationMeanIsPartial_OnsetsStayComplete()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        for (var i = 0; i < Cap; i++)
            MedicineFamily.HediffAdded(s, new HediffFacts { Id = "Hediff_" + i, PawnId = "Thing_A", Disease = true }, 0);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "Hediff_X", PawnId = "Thing_A", Disease = true }, 0);
        MedicineFamily.HediffCured(s, "Hediff_X", Day);
        var o = Save(acc, 2 * Day);
        Partial(o, MedicineFamily.DiseaseDurationMean);
        NotPartial(o, MedicineFamily.DiseaseOnsets, MedicineFamily.InfectionRecoveries, MedicineFamily.InfectionDurationMax);
        Assert.Equal(Cap + 1, V(o, MedicineFamily.DiseaseOnsets).IntegerValue);
    }

    [Fact]
    public void Medicine_InfectionTrackerFull_RecoveriesAndDurationArePartial()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        for (var i = 0; i < Cap; i++)
            MedicineFamily.HediffAdded(s, new HediffFacts { Id = "Hediff_" + i, PawnId = "Thing_A", Infection = true }, 0);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "Hediff_X", PawnId = "Thing_A", Infection = true }, 0);
        MedicineFamily.HediffCured(s, "Hediff_X", Day);
        var o = Save(acc, 2 * Day);
        Partial(o, MedicineFamily.InfectionRecoveries, MedicineFamily.InfectionDurationMax);
        NotPartial(o, MedicineFamily.DiseaseDurationMean);
    }

    // ----- retained membership kidnapping tracker -----

    [Fact]
    public void Membership_KidnapTrackerFull_RescuesAndCaptivityArePartial()
    {
        var (acc, s) = NewGame(MembershipFamily.Family);
        for (var i = 0; i < Cap; i++)
            MembershipFamily.Kidnapped(s, "Thing_K" + i, 0);
        MembershipFamily.Kidnapped(s, "Thing_X", 0);
        MembershipFamily.KidnapReturned(s, "Thing_X", Day);
        var o = Save(acc, 2 * Day);
        Partial(o, MembershipFamily.KidnapRescues, MembershipFamily.CaptiveMax);
        NotPartial(o, MembershipFamily.Kidnaps);
        Assert.Equal(Cap + 1, V(o, MembershipFamily.Kidnaps).IntegerValue);
    }

    // ----- raid -----

    private static readonly string[] RaidOutcomeOutputs =
    {
        RaidFamily.KillsMax, RaidFamily.DurationMean, RaidFamily.DurationMax, RaidFamily.DurationMinRepelled, RaidFamily.RepelledNoDown,
        RaidFamily.Retreated, RaidFamily.Wiped, RaidFamily.RepelledRate,
    };

    [Fact]
    public void Raid_MemberTrackerFull_EveryOutcomeOutputIsPartial_EvenWithNoDecidedRaid()
    {
        var (acc, s) = NewGame(RaidFamily.Family);
        RaidFamily.Arrived(s, new RaidArrival { MapId = "Map_0", PawnIds = Enumerable.Range(0, Cap + 1).Select(i => "Thing_R" + i).ToList() }, 100);
        var o = Save(acc, Day);
        Partial(o, RaidOutcomeOutputs);
        NotPartial(o, RaidFamily.Raids, RaidFamily.PawnsMax, RaidFamily.LongestGap);
    }

    [Fact]
    public void Raid_RaidTrackerFull_TheNextRaidIsNotHalfTracked()
    {
        var (acc, s) = NewGame(RaidFamily.Family);
        // The same raider in every raid keeps the member tracker at one key while the raids fill theirs.
        for (var i = 0; i < Cap; i++)
            RaidFamily.Arrived(s, new RaidArrival { MapId = "Map_0", PawnIds = new[] { "Thing_S" } }, 100);
        RaidFamily.Arrived(s, new RaidArrival { MapId = "Map_0", PawnIds = new[] { "Thing_X" } }, 100);
        Assert.False(RaidFamily.IsMember(s, "Thing_X"));   // a raider of an untracked raid would never be released
        var o = Save(acc, Day);
        Partial(o, RaidOutcomeOutputs);
        Assert.Equal(Cap + 1, V(o, RaidFamily.Raids).IntegerValue);
    }

    [Fact]
    public void Raid_BelowTheCap_OutcomeOutputsStayComplete()
    {
        var (acc, s) = NewGame(RaidFamily.Family);
        RaidFamily.Arrived(s, new RaidArrival { MapId = "Map_0", PawnIds = new[] { "Thing_R0" } }, 100);
        RaidFamily.MemberLost(s, "Thing_R0", RaidLoss.Exited, Day);
        var o = Save(acc, 2 * Day);
        Complete(o, RaidOutcomeOutputs);
        Assert.Equal(1, V(o, RaidFamily.Retreated).IntegerValue);
    }

    [Fact]
    public void Raid_MapCodesAreReleasedWithTheMap_AndNeverReused()
    {
        var (acc, s) = NewGame(RaidFamily.Family);
        RaidFamily.Arrived(s, new RaidArrival { MapId = "Map_A", PawnIds = new[] { "Thing_A" } }, 100);
        RaidFamily.Arrived(s, new RaidArrival { MapId = "Map_B", PawnIds = new[] { "Thing_B" } }, 100);
        RaidFamily.MapRemoved(s, "Map_A", 200);
        RaidFamily.Arrived(s, new RaidArrival { MapId = "Map_C", PawnIds = new[] { "Thing_C" } }, 300);
        // A colonist downed on map C must not mark the raid on map B (a reused code would).
        RaidFamily.ColonistDowned(s, "Map_C");
        RaidFamily.MemberLost(s, "Thing_B", RaidLoss.Exited, Day);
        RaidFamily.MemberLost(s, "Thing_C", RaidLoss.Exited, Day);
        var o = Save(acc, 2 * Day);
        Assert.Equal(2, V(o, RaidFamily.Retreated).IntegerValue);
        Assert.Equal(1, V(o, RaidFamily.RepelledNoDown).IntegerValue);   // B only: C had a downed colonist
        Assert.Empty(s.Active.GetValueOrDefault("map_codes")?.Keys.Where(k => k == "Map_A") ?? Enumerable.Empty<string>());
    }

    // ----- work -----

    [Fact]
    public void Work_SownRetainsAnExactTotalWithoutTheRetiredPlantDeathCohort()
    {
        var (acc, s) = NewGame(WorkFamily.Family);
        for (var i = 0; i <= Cap; i++)
            WorkFamily.Sown(s, "PlantRice");
        var o = Save(acc, Day);
        NotPartial(o, WorkFamily.Sown_);
        Assert.Equal(Cap + 1, V(o, WorkFamily.Sown_).IntegerValue);
        Assert.Null(MetricRegistry.Find("work.crops.loss_rate"));
    }

    // ----- health and death: a breakdown's cap never shortens a total -----

    [Fact]
    public void HealthDeath_CauseBreakdownFull_TheDeathTotalsStayExact()
    {
        var (acc, s) = NewGame(HealthDeathFamily.Family);
        for (var i = 0; i <= Cap; i++)
            HealthDeathFamily.Died(s, new DeathFacts
            {
                PawnId = "Thing_C" + i, FreeColonist = true, VictimKind = DeathFacts.Humanlike, Cause = "hediff:Cause" + i,
                Killer = KillerSide.Unknown,
            }, 100 + i);
        var o = Save(acc, Day);
        Assert.Equal(Cap + 1, V(o, HealthDeathFamily.DeathsByRaid).IntegerValue);
        Assert.Equal(Cap + 1, V(o, HealthDeathFamily.DeathCauses).IntegerValue);
        Complete(o, HealthDeathFamily.DeathsByRaid, HealthDeathFamily.DeathCauses);
        Assert.Equal(MetricCoverage.Partial, o.Metrics[HealthDeathFamily.DeathCauses].Values["hediff:Cause0"].Coverage);
    }
}
