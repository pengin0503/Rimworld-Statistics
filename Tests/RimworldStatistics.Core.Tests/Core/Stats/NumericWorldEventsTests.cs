namespace RimworldStatistics.Core.Tests;

/// <summary>Phase C4: medicine, disasters and caravans (ledger sections of the same names; plan C4 table).</summary>
public sealed class NumericWorldEventsTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int Day = 60000;

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

    private static Dictionary<string, MetricValue> B(MetricObservation o, string key)
        => o.Metrics[key].Values.Where(kv => kv.Key != MetricEntry.Scalar).ToDictionary(kv => kv.Key, kv => kv.Value);

    // ----- medicine -----

    [Fact]
    public void Tends_OnlyTreatmentsInvolvingTheColonyAffectTotals()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.Tended(s, null, colonyPatient: false, neutralPatient: true, new[] { 0.1 }, medicineUsed: 3);
        MedicineFamily.Tended(s, null, colonyPatient: false, neutralPatient: false, new[] { 0.2 }, medicineUsed: 2);
        MedicineFamily.Tended(s, "Thing_Doc", colonyPatient: false, neutralPatient: true, new[] { 0.5 }, medicineUsed: 1);
        MedicineFamily.Tended(s, null, colonyPatient: true, neutralPatient: false, new[] { 0.75 }, medicineUsed: 1);
        MedicineFamily.Tended(s, null, colonyPatient: true, neutralPatient: true, new[] { 0.625 }, medicineUsed: 0);

        var o = Save(acc, 1000);
        Assert.Equal(3L, V(o, MedicineFamily.Tends).IntegerValue);
        Assert.Equal(2L, V(o, MedicineFamily.NeutralTends).IntegerValue);
        Assert.Equal(2L, V(o, MedicineFamily.MedicineUsed).IntegerValue);
        Assert.Equal(0.625, V(o, MedicineFamily.TendQualityMean).RealValue);
        Assert.Equal(1L, V(o, MedicineFamily.BestTender).IntegerValue);
    }

    [Fact]
    public void Tends_OneTendTwoPartsAndSurgeries()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.Tended(s, "Thing_Doc", colonyPatient: true, neutralPatient: false, new[] { 0.6, 0.8 }, medicineUsed: 1);
        MedicineFamily.Tended(s, "Thing_Doc", colonyPatient: false, neutralPatient: true, new[] { 0.4 }, medicineUsed: 0);
        MedicineFamily.Tended(s, "Thing_Doc2", colonyPatient: true, neutralPatient: false, Array.Empty<double>(), 0);   // nothing was tended
        MedicineFamily.Surgery(s, "Thing_Doc", success: true, implant: true);
        MedicineFamily.Surgery(s, "Thing_Doc2", success: false, implant: true);   // a failed install adds no implant
        var o = Save(acc, 1000);
        Assert.Equal(2, V(o, MedicineFamily.Tends).IntegerValue);
        Assert.Equal(0.6, Math.Round(V(o, MedicineFamily.TendQualityMean).RealValue!.Value, 9));   // (0.6 + 0.8 + 0.4) ÷ 3 hediffs
        Assert.Equal(3, V(o, MedicineFamily.TendQualityMean).Denominator);
        Assert.Equal(1, V(o, MedicineFamily.NeutralTends).IntegerValue);
        Assert.Equal(1, V(o, MedicineFamily.MedicineUsed).IntegerValue);
        Assert.Equal(2, V(o, MedicineFamily.BestTender).IntegerValue);
        Assert.Equal(2, V(o, MedicineFamily.Surgeries).IntegerValue);
        Assert.Equal((1.0, 2.0), (V(o, MedicineFamily.SurgerySuccessRate).Numerator!.Value, V(o, MedicineFamily.SurgerySuccessRate).Denominator!.Value));
        Assert.Equal(1, V(o, MedicineFamily.BestSurgeon).IntegerValue);
        Assert.Equal(1, V(o, MedicineFamily.Implants).IntegerValue);
    }

    [Fact]
    public void Hediffs_OnsetsCuresAndRemovalsOtherwise()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h1", PawnId = "Thing_A", Disease = true }, 1 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h2", PawnId = "Thing_A", Infection = true, Disease = true }, 2 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h3", PawnId = "Thing_B", Infection = true }, 2 * Day);
        MedicineFamily.HediffCured(s, "h1", 4 * Day);
        MedicineFamily.HediffCured(s, "h2", 7 * Day);
        MedicineFamily.HediffGone(s, "h3");                     // the pawn died or lost the part: not a recovery
        MedicineFamily.HediffCured(s, "h3", 9 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h4", PawnId = "Thing_A", FoodPoisoning = true }, 3 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h5", PawnId = "Thing_A", Addiction = true }, 3 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h6", PawnId = "Thing_A", Overdose = true }, 3 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h7", PawnId = "Thing_A", MissingPart = true }, 3 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h8", PawnId = "Thing_A", Injury = true, WildAnimalAttack = true }, 3 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h9", PawnId = "Thing_A", Injury = true }, 3 * Day);
        MedicineFamily.HediffAdded(s, new HediffFacts { Id = "h10", PawnId = "Thing_B", Injury = true }, 3 * Day);
        MedicineFamily.Scar(s);
        var o = Save(acc, 10 * Day);
        Assert.Equal(2, V(o, MedicineFamily.DiseaseOnsets).IntegerValue);
        Assert.Equal(1, V(o, MedicineFamily.InfectionRecoveries).IntegerValue);
        Assert.Equal(4.0, V(o, MedicineFamily.DiseaseDurationMean).RealValue);   // (3 + 5) ÷ 2
        Assert.Equal(5.0, V(o, MedicineFamily.InfectionDurationMax).RealValue);
        Assert.Equal((1L, 1L, 1L, 1L), (V(o, MedicineFamily.FoodPoisonings).IntegerValue!.Value, V(o, MedicineFamily.Addictions).IntegerValue!.Value,
            V(o, MedicineFamily.Overdoses).IntegerValue!.Value, V(o, MedicineFamily.PartsLost).IntegerValue!.Value));
        Assert.Equal(1, V(o, MedicineFamily.WildAnimalInjuries).IntegerValue);
        Assert.Equal(2, V(o, MedicineFamily.InjuriesPerPawnMax).IntegerValue);
        Assert.Equal(1, V(o, MedicineFamily.Scars).IntegerValue);
    }

    [Fact]
    public void Rescues_AndResurrections()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.Rescued(s, hostile: false, away: false);
        MedicineFamily.Rescued(s, hostile: true, away: true);
        MedicineFamily.Resurrected(s, "Thing_A");
        MedicineFamily.Resurrected(s, "Thing_A");
        MedicineFamily.Resurrected(s, "Thing_B");
        var o = Save(acc, 1000);
        Assert.Equal((2L, 1L, 1L), (V(o, MedicineFamily.Rescues).IntegerValue!.Value, V(o, MedicineFamily.HostileRescues).IntegerValue!.Value, V(o, MedicineFamily.AwayRescues).IntegerValue!.Value));
        Assert.Equal((3L, 2L), (V(o, MedicineFamily.Resurrections).IntegerValue!.Value, V(o, MedicineFamily.ResurrectionsPerPawnMax).IntegerValue!.Value));
    }

    // ----- disaster -----

    [Fact]
    public void Conditions_CountStartsAndUnionTheirDays()
    {
        var (acc, s) = NewGame(DisasterFamily.Family);
        DisasterFamily.ConditionStarted(s, "ToxicFallout", "c1", 0, counted: true);
        DisasterFamily.ConditionStarted(s, "ToxicFallout", "c2", 30000, counted: true);   // a second map, overlapping
        DisasterFamily.ConditionEnded(s, "ToxicFallout", "c1", 60000);
        DisasterFamily.ConditionEnded(s, "ToxicFallout", "c2", 90000);
        DisasterFamily.ConditionEnded(s, "ToxicFallout", "c2", 95000);                     // a duplicate end
        DisasterFamily.ConditionStarted(s, "SolarFlare", "c3", 2 * Day, counted: true);    // still active at the save
        DisasterFamily.ConditionStarted(s, "ColdSnap", "c4", 2 * Day, counted: true);
        DisasterFamily.ConditionStarted(s, "Aurora", "c5", 2 * Day, counted: true);       // not a disaster
        DisasterFamily.Incident(s, "CropBlight");
        DisasterFamily.Incident(s, "MeteoriteImpact");
        DisasterFamily.Incident(s, "ShortCircuit");
        DisasterFamily.Fire(s);
        DisasterFamily.BuildingBurned(s);
        var o = Save(acc, 3 * Day);
        Assert.Equal(1.5, V(o, DisasterFamily.ToxicFalloutDays).RealValue);
        Assert.Equal(1.0, V(o, DisasterFamily.SolarFlareDays).RealValue);      // open interval through the save
        Assert.Equal((1L, 1L, 0L), (V(o, DisasterFamily.SolarFlares).IntegerValue!.Value, V(o, DisasterFamily.ColdSnaps).IntegerValue!.Value, V(o, DisasterFamily.HeatWaves).IntegerValue!.Value));
        Assert.Equal((1L, 1L, 1L), (V(o, DisasterFamily.CropBlights).IntegerValue!.Value, V(o, DisasterFamily.Meteors).IntegerValue!.Value, V(o, DisasterFamily.ShortCircuits).IntegerValue!.Value));
        Assert.Equal((1L, 1L), (V(o, DisasterFamily.Fires).IntegerValue!.Value, V(o, DisasterFamily.BuildingsBurned).IntegerValue!.Value));
    }

    [Fact]
    public void Conditions_AlreadyActiveAtALoadAreNotStarts()
    {
        var (acc, s) = NewGame(DisasterFamily.Family);
        DisasterFamily.ConditionStarted(s, "ToxicFallout", "c1", 0, counted: true);
        var d = acc.PrepareSave(1 * Day, T0, "P")!;
        // Loaded after a session without the family: the condition is running but is not a new start.
        var loaded = LiveMetricAccumulator.Resume(null, new[] { DisasterFamily.Family.Spec }, 2 * Day);
        var ls = loaded.Family(DisasterFamily.Name)!;
        DisasterFamily.Reconcile(ls, new[] { ("ToxicFallout", "c1"), ("Eclipse", "c9") }, 2 * Day);
        DisasterFamily.ConditionEnded(ls, "ToxicFallout", "c1", 3 * Day);
        var o = Save(loaded, 4 * Day);
        Assert.Equal(1.0, V(o, DisasterFamily.ToxicFalloutDays).RealValue);
        Assert.Equal(0, V(o, DisasterFamily.Eclipses).IntegerValue);      // running at the load: tracked, not a start
        // A resumed checkpoint's active condition that ended unseen is closed at the load.
        var resumed = LiveMetricAccumulator.Resume(d.Json, new[] { DisasterFamily.Family.Spec }, 1 * Day);
        DisasterFamily.Reconcile(resumed.Family(DisasterFamily.Name)!, Array.Empty<(string, string)>(), 1 * Day);
        Assert.Equal(1.0, V(Save(resumed, 5 * Day), DisasterFamily.ToxicFalloutDays).RealValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conditions_GapPreservesObservedUnionWithoutCountingTheInterruption(bool missedEvent)
    {
        var (acc, s) = NewGame(DisasterFamily.Family);
        DisasterFamily.ConditionStarted(s, "ToxicFallout", "c1", 0, counted: true);
        DisasterFamily.ConditionStarted(s, "ToxicFallout", "c2", Day / 2, counted: true);
        var saved = acc.PrepareSave(Day, T0, "P")!;
        acc.Commit(saved);

        if (missedEvent)
            acc.MarkGap(DisasterFamily.Name);
        else
            acc = LiveMetricAccumulator.Resume(saved.Json, new[] { DisasterFamily.Family.Spec }, 3 * Day);

        s = acc.Family(DisasterFamily.Name)!;
        DisasterFamily.Reconcile(s, new[] { ("ToxicFallout", "c2") }, 3 * Day);
        DisasterFamily.ConditionEnded(s, "ToxicFallout", "c2", 4 * Day);
        var days = V(Save(acc, 5 * Day), DisasterFamily.ToxicFalloutDays);
        Assert.Equal(2.0, days.RealValue);
        Assert.Equal(MetricCoverage.Partial, days.Coverage);
    }

    // ----- caravan -----

    [Fact]
    public void Caravans_DistanceUniqueTilesAndExpeditions()
    {
        var (acc, s) = NewGame(CaravanFamily.Family);
        CaravanFamily.Departed(s, "Caravan_1", 1 * Day);
        CaravanFamily.Moved(s, "Caravan_1", "10,0", adjacent: true);
        CaravanFamily.Moved(s, "Caravan_1", "11,0", adjacent: true);
        CaravanFamily.Moved(s, "Caravan_1", "10,0", adjacent: true);
        CaravanFamily.Moved(s, "Caravan_1", "99,1", adjacent: false);   // teleported (farskip)
        CaravanFamily.Returned(s, "Caravan_1", 4 * Day);
        CaravanFamily.Departed(s, "Caravan_2", 5 * Day);
        CaravanFamily.Moved(s, "Caravan_2", "12,0", adjacent: true);
        CaravanFamily.Aborted(s, "Caravan_2");                          // split: no completed expedition
        CaravanFamily.Returned(s, "Caravan_2", 9 * Day);
        CaravanFamily.Departed(s, "Caravan_3", 5 * Day);
        CaravanFamily.Lost(s, "Caravan_3");
        CaravanFamily.Lost(s, "Caravan_9");                             // never tracked
        CaravanFamily.Ambushed(s);
        CaravanFamily.SiteCleared(s);
        CaravanFamily.Abandoned(s);
        CaravanFamily.Founded(s);
        CaravanFamily.Concurrent(s, 2);
        CaravanFamily.Concurrent(s, 1);
        var o = Save(acc, 10 * Day);
        Assert.Equal(3, V(o, CaravanFamily.Departures).IntegerValue);
        Assert.Equal(4, V(o, CaravanFamily.TilesTravelled).IntegerValue);
        Assert.Equal(3, V(o, CaravanFamily.UniqueTiles).IntegerValue);   // 10, 11, 12 (the teleport target is not walked)
        Assert.Equal(3.0, V(o, CaravanFamily.LongestExpedition).RealValue);
        Assert.Equal(3, V(o, CaravanFamily.LongestDistance).IntegerValue);
        Assert.Equal(1, V(o, CaravanFamily.Lost_).IntegerValue);
        Assert.Equal((1L, 1L, 1L, 1L), (V(o, CaravanFamily.Ambushes).IntegerValue!.Value, V(o, CaravanFamily.SitesCleared).IntegerValue!.Value,
            V(o, CaravanFamily.SettlementsAbandoned).IntegerValue!.Value, V(o, CaravanFamily.SettlementsFounded).IntegerValue!.Value));
        Assert.Equal(2, V(o, CaravanFamily.ConcurrentMax).IntegerValue);
    }

    [Fact]
    public void Catalog_HasEveryC4OutputAndNothingHeld()
    {
        var keys = new[]
        {
            MedicineFamily.Rescues, MedicineFamily.Resurrections, MedicineFamily.Addictions, MedicineFamily.Tends, MedicineFamily.Surgeries,
            MedicineFamily.SurgerySuccessRate, MedicineFamily.DiseaseOnsets, MedicineFamily.InfectionRecoveries, MedicineFamily.FoodPoisonings,
            MedicineFamily.PartsLost, MedicineFamily.Implants, MedicineFamily.TendQualityMean, MedicineFamily.MedicineUsed,
            MedicineFamily.DiseaseDurationMean, MedicineFamily.InfectionDurationMax, MedicineFamily.Overdoses, MedicineFamily.Scars,
            MedicineFamily.BestSurgeon, MedicineFamily.BestTender, MedicineFamily.WildAnimalInjuries, MedicineFamily.HostileRescues,
            MedicineFamily.NeutralTends, MedicineFamily.ResurrectionsPerPawnMax, MedicineFamily.InjuriesPerPawnMax, MedicineFamily.AwayRescues,
            DisasterFamily.ToxicFalloutDays, DisasterFamily.SolarFlares, DisasterFamily.ColdSnaps, DisasterFamily.HeatWaves, DisasterFamily.VolcanicWinters,
            DisasterFamily.CropBlights, DisasterFamily.Fires, DisasterFamily.BuildingsBurned, DisasterFamily.Eclipses, DisasterFamily.Meteors,
            DisasterFamily.SolarFlareDays, DisasterFamily.ShortCircuits,
            CaravanFamily.Departures, CaravanFamily.Lost_, CaravanFamily.TilesTravelled, CaravanFamily.UniqueTiles, CaravanFamily.SitesCleared,
            CaravanFamily.SettlementsAbandoned, CaravanFamily.LongestExpedition, CaravanFamily.Ambushes, CaravanFamily.LongestDistance,
            CaravanFamily.SettlementsFounded, CaravanFamily.ConcurrentMax,
            // held until 2026-11-08: N3-H-005, N3-H-011, Q-014
            MedicineFamily.Withdrawals, MedicineFamily.HospitalStayMean, CaravanFamily.AncientDangersOpened,
        };
        foreach (var key in keys)
            Assert.True(MetricRegistry.Find(key) is { Source: MetricObservationSource.Checkpoint, Published: false }, key);
    }
}
