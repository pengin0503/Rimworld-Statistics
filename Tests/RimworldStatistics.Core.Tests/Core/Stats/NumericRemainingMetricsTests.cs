namespace RimworldStatistics.Core.Tests;

/// <summary>
/// The seven outputs held until the 2026-11-08 audit (N3-H-005, N3-H-011, N2-F-010, N1-A-008, Q-014, L-003, N1-F-004): the
/// Core decisions behind their 1.7.4985 acquisition paths, including duplicates, save/load, gaps and the "no fabricated zero"
/// rule. The game adapters only pass the facts these methods take.
/// </summary>
public sealed class NumericRemainingMetricsTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
    private const int Day = 60000;

    private static (LiveMetricAccumulator Acc, FamilyState S) NewGame(LiveFamily family)
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { family.Spec }, 0);
        return (acc, acc.Family(family.Name)!);
    }

    private static (MetricObservation Obs, string Json) SaveWithJson(LiveMetricAccumulator acc, int tick)
    {
        var d = acc.PrepareSave(tick, T0, "S" + tick)!;
        acc.Commit(d);
        return (CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "c" }, null, null), d.Json);
    }

    private static MetricObservation Save(LiveMetricAccumulator acc, int tick) => SaveWithJson(acc, tick).Obs;

    private static MetricValue V(MetricObservation o, string key) => o.Scalar(key, MetricKeys.Revision(key))!;

    // ----- N3-H-005 · withdrawal onsets -----

    [Fact]
    public void Withdrawal_EachEntryIntoTheWithdrawalStageCountsOnce()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.AddictionNeedChanged(s, "Hediff_1", withdrawal: true, 1 * Day);
        MedicineFamily.AddictionNeedChanged(s, "Hediff_1", withdrawal: true, 1 * Day);   // the same onset notified again
        MedicineFamily.AddictionNeedChanged(s, "Hediff_1", withdrawal: false, 2 * Day);  // satisfied again
        MedicineFamily.AddictionNeedChanged(s, "Hediff_1", withdrawal: false, 2 * Day);  // satisfied → desire: not an onset
        MedicineFamily.AddictionNeedChanged(s, "Hediff_1", withdrawal: true, 3 * Day);   // a new withdrawal
        MedicineFamily.AddictionNeedChanged(s, "Hediff_2", withdrawal: true, 3 * Day);   // another addiction
        var o = Save(acc, 4 * Day);
        Assert.Equal(3L, V(o, MedicineFamily.Withdrawals).IntegerValue);
        Assert.Equal(MetricCoverage.Complete, V(o, MedicineFamily.Withdrawals).Coverage);
    }

    [Fact]
    public void Withdrawal_ARemovedAddictionStopsBeingTracked_AndALoadDoesNotRecountAnOngoingWithdrawal()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.AddictionNeedChanged(s, "Hediff_1", withdrawal: true, 1 * Day);
        MedicineFamily.HediffGone(s, "Hediff_1");                                        // the addiction was removed
        MedicineFamily.AddictionNeedChanged(s, "Hediff_2", withdrawal: true, 1 * Day);
        var (_, json) = SaveWithJson(acc, 2 * Day);

        var resumed = LiveMetricAccumulator.Resume(json, new[] { MedicineFamily.Family.Spec }, 2 * Day);
        var r = resumed.Family(MedicineFamily.Name)!;
        MedicineFamily.AddictionNeedChanged(r, "Hediff_2", withdrawal: true, 2 * Day);   // still the same withdrawal after the load
        MedicineFamily.AddictionNeedChanged(r, "Hediff_1", withdrawal: true, 3 * Day);   // a new hediff with a reused id would count
        var o = Save(resumed, 4 * Day);
        Assert.Equal(3L, V(o, MedicineFamily.Withdrawals).IntegerValue);
        Assert.Equal(MetricCoverage.Complete, V(o, MedicineFamily.Withdrawals).Coverage);
    }

    [Fact]
    public void Withdrawal_StartedMidGameIsPartial_NotACompleteZero()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { MedicineFamily.Family.Spec }, 5 * Day);
        var o = Save(acc, 6 * Day);
        Assert.Equal(0L, V(o, MedicineFamily.Withdrawals).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, V(o, MedicineFamily.Withdrawals).Coverage);
    }

    // ----- N3-H-011 · medical-bed stays -----

    [Fact]
    public void HospitalStay_OneStayFromLyingDownToLeaving()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.BedEntered(s, "Thing_A", bedId: 7, 1 * Day);
        MedicineFamily.BedLeft(s, "Thing_A", died: false, 3 * Day);
        var v = V(Save(acc, 4 * Day), MedicineFamily.HospitalStayMean);
        Assert.Equal(2.0, v.RealValue);
        Assert.Equal((2.0, 1.0), (v.Numerator!.Value, v.Denominator!.Value));
        Assert.Equal(MetricCoverage.Complete, v.Coverage);
    }

    [Fact]
    public void HospitalStay_AJobSwitchInTheSameBedDoesNotSplitTheStay()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 0);
        MedicineFamily.BedLeft(s, "Thing_A", false, 1000);    // e.g. LayDown ends to watch television from the bed
        MedicineFamily.BedEntered(s, "Thing_A", 7, 1000);     // lying again in the same bed in the same tick
        MedicineFamily.BedLeft(s, "Thing_A", false, 1000 + MedicineFamily.StayMergeTicks + 1000);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 1000 + MedicineFamily.StayMergeTicks + 1000 + MedicineFamily.StayMergeTicks);
        MedicineFamily.BedLeft(s, "Thing_A", false, 3 * Day);
        var v = V(Save(acc, 4 * Day), MedicineFamily.HospitalStayMean);
        Assert.Equal(1.0, v.Denominator);
        Assert.Equal(3.0 * Day / Day, v.RealValue);
    }

    [Fact]
    public void HospitalStay_ReturningLaterOrToAnotherBedIsANewStay()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 0);
        MedicineFamily.BedLeft(s, "Thing_A", false, 1 * Day);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 1 * Day + MedicineFamily.StayMergeTicks + 1);   // got up, came back later
        MedicineFamily.BedLeft(s, "Thing_A", false, 2 * Day);
        MedicineFamily.BedEntered(s, "Thing_A", 8, 2 * Day + 10);                                  // moved to another bed at once
        MedicineFamily.BedLeft(s, "Thing_A", false, 5 * Day);
        var v = V(Save(acc, 6 * Day), MedicineFamily.HospitalStayMean);
        Assert.Equal(3.0, v.Denominator);
        Assert.Equal((1.0 * Day + (1 * Day - MedicineFamily.StayMergeTicks - 1) + (3 * Day - 10)) / Day, v.Numerator!.Value, 9);
    }

    [Fact]
    public void HospitalStay_DeathEndsNoStay_AndNoStayIsNotApplicable()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 0);
        MedicineFamily.BedLeft(s, "Thing_A", died: true, 1 * Day);
        MedicineFamily.BedLeft(s, "Thing_B", died: false, 1 * Day);    // never seen lying down: nothing
        var v = V(Save(acc, 2 * Day), MedicineFamily.HospitalStayMean);
        Assert.Null(v.RealValue);
        Assert.Equal(MetricCoverage.NotApplicable, v.Coverage);
    }

    [Fact]
    public void HospitalStay_ALeaveTooRecentToSettleWaits_AndIsCountedOnceLater()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 0);
        MedicineFamily.BedLeft(s, "Thing_A", false, 1 * Day);
        Assert.Equal(MetricCoverage.NotApplicable, V(Save(acc, 1 * Day + 10), MedicineFamily.HospitalStayMean).Coverage);
        Assert.Equal(1.0, V(Save(acc, 2 * Day), MedicineFamily.HospitalStayMean).Denominator);   // settled at the save, not yet in the totals
        MedicineFamily.BedEntered(s, "Thing_B", 9, 3 * Day);                                      // an event settles it for good
        var v = V(Save(acc, 4 * Day), MedicineFamily.HospitalStayMean);
        Assert.Equal(1.0, v.Denominator);
        Assert.Equal(1.0, v.RealValue);
    }

    [Fact]
    public void HospitalStay_AStayOpenAtASaveCompletesAfterTheLoad()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 1 * Day);
        var (first, json) = SaveWithJson(acc, 2 * Day);
        Assert.Equal(MetricCoverage.NotApplicable, V(first, MedicineFamily.HospitalStayMean).Coverage);

        var resumed = LiveMetricAccumulator.Resume(json, new[] { MedicineFamily.Family.Spec }, 2 * Day);
        MedicineFamily.BedLeft(resumed.Family(MedicineFamily.Name)!, "Thing_A", false, 5 * Day);
        var v = V(Save(resumed, 6 * Day), MedicineFamily.HospitalStayMean);
        Assert.Equal(4.0, v.RealValue);
        Assert.Equal(MetricCoverage.Complete, v.Coverage);
    }

    [Fact]
    public void HospitalStay_AGapDropsOpenStaysButKeepsSettledOnes()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 0);
        MedicineFamily.BedLeft(s, "Thing_A", false, 1 * Day);      // left, settled at the gap
        MedicineFamily.BedEntered(s, "Thing_B", 8, 1 * Day);       // still lying when the gap happens
        Save(acc, 2 * Day);
        acc.MarkGap(MedicineFamily.Name);
        MedicineFamily.BedLeft(s, "Thing_B", false, 3 * Day);      // its start is gone: no sample spans the gap
        var v = V(Save(acc, 4 * Day), MedicineFamily.HospitalStayMean);
        Assert.Equal(1.0, v.Denominator);
        Assert.Equal(1.0, v.RealValue);
        Assert.Equal(MetricCoverage.Partial, v.Coverage);
    }

    [Fact]
    public void HospitalStay_AGapProcessedLaterSettlesAStayThatEndedAfterTheLastSave()
    {
        // Review finding: MarkGap only knew the last save's tick, so a stay that ended after it was dropped as unfinished.
        var (acc, s) = NewGame(MedicineFamily.Family);
        Save(acc, 1 * Day);
        MedicineFamily.BedEntered(s, "Thing_A", 7, 1 * Day + 1000);
        MedicineFamily.BedLeft(s, "Thing_A", false, 2 * Day);
        acc.MarkGap(MedicineFamily.Name, nowTick: 3 * Day);
        var v = V(Save(acc, 4 * Day), MedicineFamily.HospitalStayMean);
        Assert.Equal(1.0, v.Denominator);
        Assert.Equal((1.0 * Day - 1000) / Day, v.RealValue!.Value, 12);
        Assert.Equal(MetricCoverage.Partial, v.Coverage);
    }

    [Fact]
    public void HospitalStay_PastTheTrackerCapIsPartial()
    {
        var (acc, s) = NewGame(MedicineFamily.Family);
        for (var i = 0; i <= FamilyOps.KeyCap; i++)
            MedicineFamily.BedEntered(s, "Thing_" + i, i, 0);
        MedicineFamily.BedLeft(s, "Thing_0", false, 1 * Day);
        Assert.Equal(MetricCoverage.Partial, V(Save(acc, 2 * Day), MedicineFamily.HospitalStayMean).Coverage);
    }

    // The N2-F-010 food shortage duration was explicitly retired; keep the
    // unrelated N1-A-008 growing-zone yield counter and its tests intact.
    [Fact]
    public void FoodStock_DurationMetricIsNotRegisteredOrReintroduced()
    {
        Assert.Null(MetricRegistry.Find("food.no_shortage.longest"));
        var (acc, _) = NewGame(FoodFamily.Family);
        var observation = Save(acc, Day);
        Assert.DoesNotContain("food.no_shortage.longest", observation.Metrics.Keys);
    }

    // ----- N1-A-008 · harvest per growing-cell day -----

    [Fact]
    public void GrowingYield_ZoneHarvestOverTheCellDayIntegral()
    {
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.GrowingCellsBaseline(s, 100, 0);
        FoodFamily.ZoneHarvested(s, 30, 2 * Day);
        for (var i = 0; i < 100; i++)
            FoodFamily.GrowingCellsChanged(s, +1, 5 * Day);
        FoodFamily.ZoneHarvested(s, 20, 7 * Day);
        var v = V(Save(acc, 10 * Day), FoodFamily.HarvestPerGrowingCell);
        Assert.Equal((50.0, 1500.0), (v.Numerator!.Value, v.Denominator!.Value));   // 100 cells × 5 days + 200 cells × 5 days
        Assert.Equal(50.0 / 1500.0, v.RealValue!.Value, 12);
        Assert.Equal(MetricCoverage.Complete, v.Coverage);
    }

    [Fact]
    public void GrowingYield_NothingCountsBeforeTheCellsAreKnown()
    {
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.ZoneHarvested(s, 30, 1 * Day);        // no baseline yet: neither side is measured
        FoodFamily.GrowingCellsChanged(s, +5, 1 * Day);
        var v = V(Save(acc, 2 * Day), FoodFamily.HarvestPerGrowingCell);
        Assert.Equal((0.0, 0.0), (v.Numerator!.Value, v.Denominator!.Value));
        Assert.Null(v.RealValue);
        Assert.Equal(MetricCoverage.NotApplicable, v.Coverage);
    }

    [Fact]
    public void GrowingYield_NoGrowingZoneIsNotApplicable_NotACompleteBlank()
    {
        // Review finding: a known count of 0 cells gave a "complete" value with no number.
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.GrowingCellsBaseline(s, 0, 0);
        var v = V(Save(acc, 2 * Day), FoodFamily.HarvestPerGrowingCell);
        Assert.Null(v.RealValue);
        Assert.Equal(MetricCoverage.NotApplicable, v.Coverage);
    }

    [Fact]
    public void GrowingYield_AGapAfterHarvestsKeepsBothSidesAligned()
    {
        // Review finding: after a mid-session gap the cell-days since the last save were lost but the harvests were kept.
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.GrowingCellsBaseline(s, 10, 0);
        Save(acc, 1 * Day);
        FoodFamily.ZoneHarvested(s, 5, 1 * Day + Day / 2);   // the integral is brought up to each counted harvest
        acc.MarkGap(FoodFamily.Name, nowTick: 2 * Day);
        FoodFamily.GrowingCellsBaseline(s, 10, 3 * Day);
        var v = V(Save(acc, 4 * Day), FoodFamily.HarvestPerGrowingCell);
        Assert.Equal((5.0, 25.0), (v.Numerator!.Value, v.Denominator!.Value));   // 10 cells × 1.5 days, then 10 × 1 day
        Assert.Equal(MetricCoverage.Partial, v.Coverage);
    }

    [Fact]
    public void GrowingYield_TheOpenSegmentIsCountedOnceAcrossALoad()
    {
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.GrowingCellsBaseline(s, 10, 0);
        var (first, json) = SaveWithJson(acc, 2 * Day);
        Assert.Equal(20.0, V(first, FoodFamily.HarvestPerGrowingCell).Denominator);

        var resumed = LiveMetricAccumulator.Resume(json, new[] { FoodFamily.Family.Spec }, 2 * Day);
        var r = resumed.Family(FoodFamily.Name)!;
        FoodFamily.GrowingCellsBaseline(r, 10, 2 * Day);   // the startup recount agrees
        FoodFamily.ZoneHarvested(r, 4, 3 * Day);
        var v = V(Save(resumed, 4 * Day), FoodFamily.HarvestPerGrowingCell);
        Assert.Equal((4.0, 40.0), (v.Numerator!.Value, v.Denominator!.Value));
        Assert.Equal(MetricCoverage.Complete, v.Coverage);
    }

    [Fact]
    public void GrowingYield_ARecountThatDisagreesMakesItPartial()
    {
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.GrowingCellsBaseline(s, 10, 0);
        FoodFamily.GrowingCellsBaseline(s, 12, 1 * Day);    // cells changed without a tracked AddCell/RemoveCell (e.g. a mod)
        FoodFamily.ZoneHarvested(s, 3, 1 * Day);
        var v = V(Save(acc, 2 * Day), FoodFamily.HarvestPerGrowingCell);
        Assert.Equal((3.0, 22.0), (v.Numerator!.Value, v.Denominator!.Value));
        Assert.Equal(MetricCoverage.Partial, v.Coverage);
    }

    [Fact]
    public void GrowingYield_AGapPausesBothSidesUntilTheNextRecount()
    {
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.GrowingCellsBaseline(s, 10, 0);
        Save(acc, 1 * Day);
        acc.MarkGap(FoodFamily.Name);                        // integral kept through the last save
        FoodFamily.ZoneHarvested(s, 7, 1 * Day + 1);        // not measured: the cells are unknown now
        FoodFamily.GrowingCellsChanged(s, +10, 1 * Day + 5);
        FoodFamily.GrowingCellsBaseline(s, 20, 2 * Day);     // the save-time recount resumes the measure
        FoodFamily.ZoneHarvested(s, 2, 2 * Day);
        var v = V(Save(acc, 3 * Day), FoodFamily.HarvestPerGrowingCell);
        Assert.Equal((2.0, 30.0), (v.Numerator!.Value, v.Denominator!.Value));
        Assert.Equal(MetricCoverage.Partial, v.Coverage);
    }

    // ----- Q-014 · ancient dangers opened -----

    [Fact]
    public void AncientDanger_AWatchedSealedShrineCountsOnceWhenItOpens()
    {
        var (acc, s) = NewGame(CaravanFamily.Family);
        var a = CaravanFamily.ShrineKey("Map_0", 10, 10, 26, 24);
        var b = CaravanFamily.ShrineKey("Map_0", 40, 40, 55, 57);
        CaravanFamily.ShrineSealed(s, a, 0);
        CaravanFamily.ShrineSealed(s, b, 0);
        CaravanFamily.ShrineSealed(s, a, 100);               // a later scan sees it again
        CaravanFamily.ShrineUnsealed(s, a);
        CaravanFamily.ShrineUnsealed(s, a);                  // more fog lifted inside: still one
        CaravanFamily.ShrineSealed(s, a, 200);               // re-registered after opening (e.g. refogged): never again
        CaravanFamily.ShrineUnsealed(s, a);
        CaravanFamily.ShrineUnsealed(s, CaravanFamily.ShrineKey("Map_0", 1, 1, 9, 9));   // never seen sealed
        var v = V(Save(acc, Day), CaravanFamily.AncientDangersOpened);
        Assert.Equal(1L, v.IntegerValue);
        Assert.Equal(MetricCoverage.Complete, v.Coverage);
        Assert.Equal(new[] { b }, CaravanFamily.WatchedShrines(s));
    }

    [Fact]
    public void AncientDanger_TheWatchSurvivesALoad_AndARemovedMapDropsIt()
    {
        var (acc, s) = NewGame(CaravanFamily.Family);
        var a = CaravanFamily.ShrineKey("Map_0", 10, 10, 26, 24);
        var gone = CaravanFamily.ShrineKey("Map_3", 10, 10, 26, 24);
        CaravanFamily.ShrineSealed(s, a, 0);
        CaravanFamily.ShrineSealed(s, gone, 0);
        var (_, json) = SaveWithJson(acc, Day);
        var resumed = LiveMetricAccumulator.Resume(json, new[] { CaravanFamily.Family.Spec }, Day);
        var r = resumed.Family(CaravanFamily.Name)!;
        CaravanFamily.ShrinesOnMapGone(r, "Map_3");
        CaravanFamily.ShrineUnsealed(r, gone);
        CaravanFamily.ShrineUnsealed(r, a);
        var v = V(Save(resumed, 2 * Day), CaravanFamily.AncientDangersOpened);
        Assert.Equal(1L, v.IntegerValue);
        Assert.Equal(MetricCoverage.Complete, v.Coverage);
    }

    [Fact]
    public void AncientDanger_AGapKeepsTheWatchedShrines_ButTheCountIsPartial()
    {
        // Review follow-up: the watch is a register of sealed structures, not an open interval; a gap must not forget it
        // (an approached shrine has no trigger left to find it again).
        var (acc, s) = NewGame(CaravanFamily.Family);
        var a = CaravanFamily.ShrineKey("Map_0", 10, 10, 26, 24);
        CaravanFamily.ShrineSealed(s, a, 0);
        Save(acc, Day);
        acc.MarkGap(CaravanFamily.Name, nowTick: 2 * Day);
        Assert.Equal(new[] { a }, CaravanFamily.WatchedShrines(s));
        CaravanFamily.ShrineUnsealed(s, a);
        var v = V(Save(acc, 3 * Day), CaravanFamily.AncientDangersOpened);
        Assert.Equal(1L, v.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, v.Coverage);
    }

    [Fact]
    public void AncientDanger_PastTheWatchCapIsPartial()
    {
        var (acc, s) = NewGame(CaravanFamily.Family);
        for (var i = 0; i <= FamilyOps.KeyCap; i++)
            CaravanFamily.ShrineSealed(s, CaravanFamily.ShrineKey("Map_0", i, 0, i, 0), 0);
        Assert.Equal(MetricCoverage.Partial, V(Save(acc, Day), CaravanFamily.AncientDangersOpened).Coverage);
    }

    [Fact]
    public void AncientDanger_ShrineKeysRoundTrip()
    {
        var key = CaravanFamily.ShrineKey("Map_12", 3, 4, 20, 21);
        Assert.True(CaravanFamily.TryParseShrineKey(key, out var map, out var minX, out var minZ, out var maxX, out var maxZ));
        Assert.Equal(("Map_12", 3, 4, 20, 21), (map, minX, minZ, maxX, maxZ));
        Assert.False(CaravanFamily.TryParseShrineKey("Map_1|x", out _, out _, out _, out _, out _));
        Assert.False(CaravanFamily.TryParseShrineKey("Map_1|5,5,4,4", out _, out _, out _, out _, out _));   // max below min
    }

    // ----- L-003 · Imperial quest successes (save-time snapshot) -----

    [Fact]
    public void ImperialQuests_AreASaveTimeSnapshotOfTheGamesInvolvedFactions()
    {
        var m = RoyaltyQuestSnapshot.Build(successes: 3, partial: false);
        var v = m[RoyaltyQuestSnapshot.Success].Values[MetricEntry.Scalar];
        Assert.Equal((3L, MetricCoverage.SnapshotOnly), (v.IntegerValue, v.Coverage));
        Assert.Equal(MetricCoverage.Partial, RoyaltyQuestSnapshot.Build(1, partial: true)[RoyaltyQuestSnapshot.Success].Values[MetricEntry.Scalar].Coverage);

        var d = MetricRegistry.Find(RoyaltyQuestSnapshot.Success)!;
        Assert.Equal((MetricObservationSource.SaveSnapshot, "royalty_quests", AcquisitionUnits.Royalty), (d.Source, d.Acquisition, d.RequiredDlc));
        Assert.Equal(AcquisitionKind.Snapshot, AcquisitionUnits.Find("royalty_quests")!.Kind);
        Assert.False(d.Published);
        Assert.Equal(2, d.Revision);   // the root allow-list definition of revision 1 is never mixed in
    }

    [Fact]
    public void ImperialQuests_AreNoLongerGuessedFromRetainedQuestRoots()
    {
        var m = new Dictionary<string, MetricEntry>();
        RetainedQuestMetricReader.Summarize(new List<RetainedQuest>
        {
            new(1, "EndGame_RoyalAscent", AcceptanceTick: 100, Ended: true, Outcome: "Success"),
            new(2, "BestowingCeremony", AcceptanceTick: 120, Ended: true, Outcome: "Success"),
        }, 0, 60000, m);
        Assert.False(m.ContainsKey(RoyaltyQuestSnapshot.Success));
        Assert.Equal(2L, m[MetricKeys.QuestSuccess].Values[MetricEntry.Scalar].IntegerValue);
    }

    // ----- N1-F-004 · smallest party at an ending -----

    [Fact]
    public void EndingParty_OnlyEndingsWithAParticipationDefinitionCarryIt()
    {
        var c = new CreditsContext();
        c.Begin(EndingType.Ship, launchedBefore: 2, participantsDefined: true);
        Assert.Equal(3, c.Resolve(5, null)!.Party);
        c.Begin(EndingType.Ship, 2);                         // dev-mode countdown without a ship
        Assert.Null(c.Resolve(2, null)!.Party);
        c.Begin(EndingType.CerebrexDestroyed, 0);
        Assert.Null(c.Resolve(0, null)!.Party);
        Assert.Null(c.Resolve(0, "credits")!.Party);          // nothing set up: other

        var lineage = new List<LineageEntry>();
        var ev = EndingLedger.Record("cid", lineage, new EndingSignal(EndingType.Archonexus, 4, null, Party: 4), EndingSource.Live, 100, 4, false, null, T0);
        Assert.Equal(4, ev.PartyAtEnding);
        Assert.Null(EndingLedger.Record("cid", lineage, new EndingSignal(EndingType.Ship, 0, null), EndingSource.Live, 200, 4, false, null, T0).PartyAtEnding);
    }

    [Fact]
    public void EndingParty_TheFirstRecordedPartyStays()
    {
        var p = new Profile();
        ProfileMutations.UpsertEnding(p, new EndingEvent { CampaignId = "c", Type = EndingType.Ship, Source = EndingSource.SeededTrace });
        ProfileMutations.UpsertEnding(p, new EndingEvent { CampaignId = "c", Type = EndingType.Ship, Source = EndingSource.Live, PartyAtEnding = 2 });
        ProfileMutations.UpsertEnding(p, new EndingEvent { CampaignId = "c", Type = EndingType.Ship, Source = EndingSource.Live, PartyAtEnding = 5 });
        Assert.Equal(2, p.EndingEvents.Single().PartyAtEnding);
    }

    private static ColonyIdentity Id(string seed) => new()
    {
        SeedString = seed, PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 7,
        StartingPawnIds = new List<string> { "Thing_" + seed }, ModCampaignId = "party-" + seed,
    };

    private static SaveObservation V1(string seed) => new()
    {
        FileName = seed + ".rws", FileSize = 1, LastWriteUtc = T0, ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
        Identity = Id(seed), Values = { TicksGame = 700000 },
    };

    private static EndingEvent Ending(string seed, EndingType type, int? party, int ordinal = 0, bool dev = false)
        => new() { CampaignId = "party-" + seed, Type = type, Ordinal = ordinal, Source = EndingSource.Live, TicksGame = 600000, DevMode = dev, PartyAtEnding = party };

    private static NumericRow MinParty(Profile p, bool includeDev = false)
    {
        var colonies = CampaignMatcher.BuildColonies(p);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions { IncludeDevMode = includeDev });
        return MetricCalculator.BuildView(p, colonies, stats, MetricRegistry.Catalog).Overall.Single(r => r.Definition.Key == MetricRankings.MinPartyAtEnding);
    }

    private static string KeyOf(Profile p, string seed) => CampaignMatcher.BuildColonies(p).Single(c => c.CampaignIds().Contains("party-" + seed)).Key;

    [Fact]
    public void EndingParty_MinimumOverEndingsWithAKnownParty_AndUnknownWhenNoneHasOne()
    {
        var p = new Profile();
        foreach (var seed in new[] { "A", "B", "C", "D" })
            p.SaveObservations.Add(V1(seed));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 3));
        p.EndingEvents.Add(Ending("B", EndingType.RoyalAscent, 1));
        p.EndingEvents.Add(Ending("C", EndingType.Archonexus, null));        // recorded before the party existed
        p.EndingEvents.Add(Ending("D", EndingType.CerebrexDestroyed, null));  // no participation definition: not a target
        var row = MinParty(p);
        Assert.Equal((1d, KeyOf(p, "B")), (row.Value.Number, row.Value.ColonyKey));
        Assert.Equal((3, 2), (row.Targets, row.Valid));
        Assert.Equal(MetricCoverage.Partial, row.Value.Coverage);
        Assert.DoesNotContain(MetricRegistry.All, d => d.Key == MetricRankings.MinPartyAtEnding);

        var none = new Profile();
        none.SaveObservations.Add(V1("A"));
        none.EndingEvents.Add(Ending("A", EndingType.Ship, null));
        Assert.Null(MinParty(none).Value.Number);
    }

    [Fact]
    public void EndingParty_DevModeEndingsFollowTheEndingFilter()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A"));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 1, dev: true));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 4, ordinal: 1));
        Assert.Equal(4d, MinParty(p).Value.Number);
        Assert.Equal(MetricCoverage.Complete, MinParty(p).Value.Coverage);
        Assert.Equal(1d, MinParty(p, includeDev: true).Value.Number);
    }

    // ----- catalog -----

    [Fact]
    public void NewOutputs_AreCatalogedUnderUnverifiedUnits()
    {
        var expected = new Dictionary<string, (string Ledger, string Unit)>
        {
            [MedicineFamily.Withdrawals] = ("N3-H-005", MedicineFamily.Name),
            [MedicineFamily.HospitalStayMean] = ("N3-H-011", MedicineFamily.Name),
            [FoodFamily.HarvestPerGrowingCell] = ("N1-A-008", FoodFamily.Name),
            [CaravanFamily.AncientDangersOpened] = ("Q-014", CaravanFamily.Name),
            [RoyaltyQuestSnapshot.Success] = ("L-003", "royalty_quests"),
            [MetricRankings.MinPartyAtEnding] = ("N1-F-004", "ending_party"),
        };
        foreach (var (key, (ledger, unit)) in expected)
        {
            var d = MetricRegistry.Find(key);
            Assert.True(d != null, key + " is not in the catalog");
            Assert.Equal((ledger, unit), (d!.LedgerIds, d.Acquisition));
            Assert.Equal(AcquisitionStatus.Unverified, AcquisitionUnits.Find(unit)!.Status);
            Assert.False(d.Published);
        }
        Assert.Equal(MetricUnit.PerCellDay, MetricRegistry.Find(FoodFamily.HarvestPerGrowingCell)!.Unit);
        // R-011 stays unsupported (by exact ledger id: N4-R-011 is a different candidate).
        Assert.DoesNotContain(MetricRegistry.Catalog, d => d.LedgerIds.Split(',').Select(x => x.Trim()).Contains("R-011"));
    }

    [Fact]
    public void FamiliesWithNewOutputs_StartOverFromOlderRevisions()
    {
        // A checkpoint of the earlier revision did not measure the new outputs: continuing it would claim them complete.
        foreach (var family in new[] { MedicineFamily.Family, FoodFamily.Family, CaravanFamily.Family })
        {
            var old = LiveMetricAccumulator.StartNew(new[] { new FamilySpec(family.Name, 1) }, 0);
            var json = old.PrepareSave(Day, T0, "old")!.Json;
            var resumed = LiveMetricAccumulator.Resume(json, new[] { family.Spec }, Day);
            Assert.True(family.Revision >= 2);
            Assert.Equal(Day, resumed.Family(family.Name)!.SinceTick);
        }
    }
}
