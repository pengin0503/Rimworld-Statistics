using System.Diagnostics;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// The colony chronicle (V2-A, spec §4.5): one save branch at a time, nothing counted twice, nothing dated without proof.
/// </summary>
public sealed class ColonyChronicleTests
{
    private const string Campaign = "camp-chronicle-1";
    private const string Version = "1.7.4985 rev1269";
    private static readonly string HashA = new('a', 64);
    private static readonly DateTime Epoch = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Deaths = MetricRegistry.Find(HealthDeathFamily.DeathsByRaid)!;
    private static readonly MetricDefinition Raids = MetricRegistry.Find(RaidFamily.ByArrivalMode)!;

    [Fact]
    public void EveryIntervalCounterIsARunningTotalOfEvents()
    {
        foreach (var key in ColonyChronicle.IntervalCounterMetricKeys)
        {
            var definition = MetricRegistry.Find(key);
            Assert.NotNull(definition);
            Assert.True(HistoryChanges.IsRunningTotal(definition!), $"{key} is not a running total");
        }
    }

    [Fact]
    public void MissingColonyReturnsNull()
    {
        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult { Colonies = [], EndingsByType = new Dictionary<EndingType, int>() },
            Numeric = NumericView.Empty,
        };

        Assert.Null(ColonyChronicle.Build(view, "id:nobody", ColonySaveLineage.Empty("id:nobody")));
    }

    [Fact]
    public void LinearChain_ShowsTheEndingOnceAndCountsEachIntervalOnce()
    {
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("s2.rws", 120_000);
        f.Save("s3.rws", 180_000, new LineageEntry(EndingType.Ship, 0, 150_000));
        // A byte-identical copy of the latest save (a reload, or a copied file) holds the same checkpoint.
        f.Save("s3-copy.rws", 180_000, new LineageEntry(EndingType.Ship, 0, 150_000)).ContentHash = "content-s3.rws";
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"], (Deaths, Counter(0)), (Raids, Counter(1)));
        f.Checkpoint("cp2", "cp1", 120_000, ["s2.rws"], (Deaths, Counter(1)), (Raids, Counter(3)));
        f.Checkpoint("cp3", "cp2", 180_000, ["s3.rws", "s3-copy.rws"], (Deaths, Counter(1)), (Raids, Counter(4)));
        f.Ending(EndingType.Ship, 0, 150_000, departed: 4);

        var (chronicle, _) = f.Build();

        var ending = Assert.Single(chronicle.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.Equal(150_000, ending.EndTick);
        Assert.Equal(ChronicleTimingKind.ExactTick, ending.TimingKind);
        Assert.Equal(ChronicleEvidenceSource.LiveEvent, ending.EvidenceSource);
        Assert.Equal(4, ending.DepartedCount);

        var intervals = chronicle.Entries.Where(e => e.Kind == ChronicleEntryKind.VerifiedInterval).ToArray();
        Assert.Equal(2, intervals.Length);
        Assert.Equal((60_000, 120_000), (intervals[0].StartTick, intervals[0].EndTick));
        Assert.Equal((120_000, 180_000), (intervals[1].StartTick, intervals[1].EndTick));
        Assert.All(intervals, i => Assert.Equal(ChronicleTimingKind.ProvenInterval, i.TimingKind));

        // The intervals add up to the change over the whole chain, so nothing was counted twice.
        double Sum(string key) => intervals.SelectMany(i => i.IntervalFacts).Where(x => x.MetricKey == key).Sum(x => x.Delta);
        Assert.Equal(3, Sum(Raids.Key));
        Assert.Equal(1, Sum(Deaths.Key));
        Assert.DoesNotContain(intervals[1].IntervalFacts, x => x.MetricKey == Deaths.Key);   // no change is not a fact

        Assert.DoesNotContain(chronicle.Entries, e => e.GapKind == ChronicleGapKind.IntervalNotCounted);
        Assert.Empty(chronicle.UndatedTraces);
        Assert.Equal(0, chronicle.OtherBranchEndingCount);
        Assert.Equal(0, chronicle.AmbiguousBranchEndingCount);
        Assert.Equal(chronicle.Entries.Count, chronicle.Entries.Select(e => e.StableKey).Distinct().Count());
        Assert.Equal(chronicle.Entries.OrderBy(e => e.EndTick).Select(e => e.StableKey), chronicle.Entries.Select(e => e.StableKey));
    }

    [Fact]
    public void RedoOnAnotherBranch_KeepsEachBranchsOwnEnding()
    {
        // Branch A reached the ending at tick 100,000. The player then loaded the day-1 save and reached the same ending
        // (same ordinal) again at tick 110,000 on branch B; the ending ledger keeps only the first of the two.
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("a.rws", 120_000, new LineageEntry(EndingType.Ship, 0, 100_000));
        f.Save("b.rws", 130_000, new LineageEntry(EndingType.Ship, 0, 110_000));
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"]);
        f.Checkpoint("cpA", "cp1", 120_000, ["a.rws"]);
        f.Checkpoint("cpB", "cp1", 130_000, ["b.rws"]);
        f.Ending(EndingType.Ship, 0, 100_000);

        var (_, lineage) = f.Build();
        var (onA, _) = f.Build(KeyOf(lineage, "cpA"));
        var (onB, _) = f.Build(KeyOf(lineage, "cpB"));

        var a = Assert.Single(onA.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.Equal(100_000, a.EndTick);
        Assert.Equal(ChronicleEvidenceSource.LiveEvent, a.EvidenceSource);
        Assert.Equal(0, onA.OtherBranchEndingCount);
        Assert.Equal("cpA", onA.SelectedBranch!.CheckpointId);

        var b = Assert.Single(onB.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.Equal(110_000, b.EndTick);
        Assert.Equal(ChronicleEvidenceSource.RecoveredLineage, b.EvidenceSource);
        Assert.Equal(MetricCoverage.Partial, b.Coverage);
        Assert.False(b.DevMode);
        Assert.Equal(1, onB.OtherBranchEndingCount);   // branch A's ending, proven elsewhere by branch B's own save
        Assert.Empty(onB.UndatedTraces);
    }

    [Fact]
    public void EndingNoSaveHolds_IsShownOnlyWhereEveryOtherBranchRulesItOut()
    {
        // No save holds the ending at tick 125,000. Branch B was saved after it without it, so it can only be on branch A.
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("a.rws", 120_000);
        f.Save("b.rws", 130_000);
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"]);
        f.Checkpoint("cpA", "cp1", 120_000, ["a.rws"]);
        f.Checkpoint("cpB", "cp1", 130_000, ["b.rws"]);
        f.Ending(EndingType.RoyalAscent, 0, 125_000);

        var (_, lineage) = f.Build();
        var (onA, _) = f.Build(KeyOf(lineage, "cpA"));
        var (onB, _) = f.Build(KeyOf(lineage, "cpB"));

        Assert.Equal(125_000, Assert.Single(onA.Entries, e => e.Kind == ChronicleEntryKind.Ending).EndTick);
        Assert.DoesNotContain(onB.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.Equal(1, onB.OtherBranchEndingCount);
    }

    [Fact]
    public void EndingNoSaveSettles_IsCountedAsAmbiguousAndShownOnNoBranch()
    {
        // Neither branch was saved after tick 125,000, so the ending could belong to either one.
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("a.rws", 120_000);
        f.Save("b.rws", 122_000);
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"]);
        f.Checkpoint("cpA", "cp1", 120_000, ["a.rws"]);
        f.Checkpoint("cpB", "cp1", 122_000, ["b.rws"]);
        f.Ending(EndingType.RoyalAscent, 0, 125_000);

        var (_, lineage) = f.Build();
        foreach (var id in new[] { "cpA", "cpB" })
        {
            var (chronicle, _) = f.Build(KeyOf(lineage, id));
            Assert.DoesNotContain(chronicle.Entries, e => e.Kind == ChronicleEntryKind.Ending);
            Assert.Equal(1, chronicle.AmbiguousBranchEndingCount);
            Assert.Equal(0, chronicle.OtherBranchEndingCount);
        }
    }

    [Fact]
    public void LateStartAndInterruptions_AreGapsAndNoIntervalSpansThem()
    {
        // Recording began at tick 30,000 (MOD added mid-game); the health family was interrupted after the second save.
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("s2.rws", 120_000);
        f.Save("s3.rws", 180_000);
        f.Save("s4.rws", 240_000);
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"], (Deaths, Counter(1, since: 30_000)));
        f.Checkpoint("cp2", "cp1", 120_000, ["s2.rws"], (Deaths, Counter(2, since: 30_000)));
        f.Checkpoint("cp3", "cp2", 180_000, ["s3.rws"], (Deaths, Interrupted(3, since: 30_000)));
        f.Checkpoint("cp4", "cp3", 240_000, ["s4.rws"], (Deaths, Interrupted(5, since: 30_000)));

        var (chronicle, _) = f.Build();

        var late = Assert.Single(chronicle.Entries, e => e.GapKind == ChronicleGapKind.LiveRecordingStartedLateOrReset);
        Assert.Equal(30_000, late.EndTick);

        var interval = Assert.Single(chronicle.Entries, e => e.Kind == ChronicleEntryKind.VerifiedInterval);
        Assert.Equal((60_000, 120_000), (interval.StartTick, interval.EndTick));
        var fact = Assert.Single(interval.IntervalFacts);
        Assert.Equal(1, fact.Delta);
        Assert.Equal(MetricCoverage.Partial, fact.Coverage);   // counted only since recording began

        // Consecutive intervals that could not be counted are one span, and nothing is claimed inside it.
        var notCounted = Assert.Single(chronicle.Entries, e => e.GapKind == ChronicleGapKind.IntervalNotCounted);
        Assert.Equal((120_000, 240_000), (notCounted.StartTick, notCounted.EndTick));
        Assert.Equal(ChronicleTimingKind.ProvenInterval, notCounted.TimingKind);
    }

    [Fact]
    public void FamilyNoLongerRecordedWithoutAGap_MarksTheIntervalNotCounted()
    {
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("s2.rws", 120_000);
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"], (Raids, Counter(1))).ContinuousFamilies = [HealthDeathFamily.Name, RaidFamily.Name];
        f.Checkpoint("cp2", "cp1", 120_000, ["s2.rws"], (Raids, Counter(3))).ContinuousFamilies = [RaidFamily.Name];

        var (chronicle, _) = f.Build();

        var interval = Assert.Single(chronicle.Entries, e => e.Kind == ChronicleEntryKind.VerifiedInterval);
        Assert.Equal(2, Assert.Single(interval.IntervalFacts, x => x.MetricKey == Raids.Key).Delta);
        var gap = Assert.Single(chronicle.Entries, e => e.GapKind == ChronicleGapKind.IntervalNotCounted);
        Assert.Equal((60_000, 120_000), (gap.StartTick, gap.EndTick));
    }

    [Fact]
    public void SaveWithoutCheckpoint_GivesOnlyHistoryMilestonesAndTheUnprovenGap()
    {
        var f = new Fixture();
        f.Save("only.rws", 900_000);
        f.SaveFileMetrics("only.rws", 900_000, (MetricRegistry.Find(MetricKeys.WealthTotalFirst100K)!, new MetricValue
        {
            Coverage = MetricCoverage.SnapshotOnly,
            RealValue = 12.5,
        }));

        var (chronicle, _) = f.Build();

        Assert.All(chronicle.Entries, e => Assert.True(
            e.Kind == ChronicleEntryKind.Milestone && e.TimingKind == ChronicleTimingKind.HistorySampleDay
            || e.GapKind == ChronicleGapKind.UnprovenSaveWithoutCheckpoint,
            $"unexpected entry {e.StableKey}"));
        var gap = Assert.Single(chronicle.Entries, e => e.GapKind == ChronicleGapKind.UnprovenSaveWithoutCheckpoint);
        Assert.Equal(900_000, gap.EndTick);
        var milestone = Assert.Single(chronicle.Entries, e => e.MilestoneKind == ChronicleMilestoneKind.WealthFirst100K);
        Assert.Equal(750_000, milestone.EndTick);
        Assert.Equal(ChronicleEvidenceSource.SaveHistoryGraph, milestone.EvidenceSource);
    }

    [Fact]
    public void SeededRecoveredAndUnexplainedTraces_StayOffTheTimeline()
    {
        var f = new Fixture();
        var save = f.Save("s1.rws", 60_000,
            new LineageEntry(EndingType.Ship, 0, 60_000),          // seeded at the load tick, matching the seeded record
            new LineageEntry(EndingType.RoyalAscent, 0, 40_000));  // a lineage entry the profile holds no record of
        save.EndingTraces.Add(EndingType.Archonexus);
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"]);
        f.Ending(EndingType.Ship, 0, 60_000, source: EndingSource.SeededTrace);

        var (chronicle, _) = f.Build();

        Assert.DoesNotContain(chronicle.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.Equal(
            new[]
            {
                (EndingType.Ship, ChronicleEvidenceSource.SeededTrace),
                (EndingType.RoyalAscent, ChronicleEvidenceSource.RecoveredLineage),
                (EndingType.Archonexus, ChronicleEvidenceSource.SaveTrace),
            },
            chronicle.UndatedTraces.Select(t => (t.EndingType, t.EvidenceSource)).ToArray());
    }

    [Fact]
    public void DevModeEnding_IsShownOnlyWhenDevModeEndingsAreIncluded()
    {
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("s2.rws", 120_000, new LineageEntry(EndingType.Ship, 0, 90_000));
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"]);
        f.Checkpoint("cp2", "cp1", 120_000, ["s2.rws"]);
        f.Ending(EndingType.Ship, 0, 90_000, dev: true);

        var (hidden, _) = f.Build(includeDev: false);
        var (shown, _) = f.Build(includeDev: true);

        Assert.DoesNotContain(hidden.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.Empty(hidden.UndatedTraces);   // the save's own entry is that same hidden record, not a new trace
        Assert.True(Assert.Single(shown.Entries, e => e.Kind == ChronicleEntryKind.Ending).DevMode);
    }

    [Fact]
    public void CarriedCheckpointSave_IsTheDefaultEndAndItsUnrecordedSpanIsAGap()
    {
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("s2.rws", 120_000);
        f.Save("s3.rws", 180_000, new LineageEntry(EndingType.Ship, 0, 150_000));   // saved with the old checkpoint
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"]);
        f.Checkpoint("cp2", "cp1", 120_000, ["s2.rws", "s3.rws"]);
        f.Ending(EndingType.Ship, 0, 150_000);

        var (chronicle, lineage) = f.Build();

        Assert.Equal(SaveLineageNodeKind.CarriedCheckpointSave, chronicle.SelectedBranch!.Kind);
        Assert.Equal(180_000, chronicle.SelectedBranch.SaveTick);
        Assert.Equal(3, chronicle.BranchNodeChain.Count);
        var carried = Assert.Single(chronicle.Entries, e => e.GapKind == ChronicleGapKind.CarriedCheckpointUnrecordedSpan);
        Assert.Equal((120_000, 180_000), (carried.StartTick, carried.EndTick));
        Assert.Equal(150_000, Assert.Single(chronicle.Entries, e => e.Kind == ChronicleEntryKind.Ending).EndTick);

        // The checkpoint itself, as picked in the lineage view, ends before the carried save and so before the ending.
        var (atCheckpoint, _) = f.Build(KeyOf(lineage, "cp2"));
        Assert.Equal(SaveLineageNodeKind.Checkpoint, atCheckpoint.SelectedBranch!.Kind);
        Assert.DoesNotContain(atCheckpoint.Entries, e => e.GapKind == ChronicleGapKind.CarriedCheckpointUnrecordedSpan);
    }

    [Fact]
    public void BranchKey_FindsTheSameBranchAfterTheLineageChangesAndFallsBackWhenGone()
    {
        var f = new Fixture();
        f.Save("s1.rws", 60_000);
        f.Save("a.rws", 120_000);
        f.Save("b.rws", 130_000);
        f.Checkpoint("cp1", null, 60_000, ["s1.rws"]);
        f.Checkpoint("cpA", "cp1", 120_000, ["a.rws"]);
        f.Checkpoint("cpB", "cp1", 130_000, ["b.rws"]);
        var (_, before) = f.Build();
        var keyA = KeyOf(before, "cpA");
        var indexBefore = before.Nodes.Single(n => n.CheckpointId == "cpA").Index;

        // An older save imported later shifts every node index.
        f.Save("s0.rws", 30_000);
        f.Checkpoint("cp0", null, 30_000, ["s0.rws"]);
        var (onA, after) = f.Build(keyA);

        Assert.NotEqual(indexBefore, after.Nodes.Single(n => n.CheckpointId == "cpA").Index);
        Assert.False(onA.RequestedBranchMissing);
        Assert.Equal("cpA", onA.SelectedBranch!.CheckpointId);
        Assert.Equal(keyA, onA.SelectedBranch.Key);

        var (fallback, _) = f.Build(keyA with { CheckpointId = "cp-gone" });
        Assert.True(fallback.RequestedBranchMissing);
        Assert.NotNull(fallback.SelectedBranch);
        Assert.True(fallback.SelectedBranch!.IsRepresentative);
    }

    [Fact]
    public void EntryCap_KeepsEndingsMilestonesAndGapsAndCountsWhatItLeavesOut()
    {
        var f = new Fixture();
        f.Save("s1.rws", 6_000_000, new LineageEntry(EndingType.Ship, 0, 3_000_000));
        var research = new MetricEntry { Revision = MetricKeys.Revision(ResearchFamily.CompletedTicks) };
        research.Values[MetricEntry.Scalar] = new MetricValue { Coverage = MetricCoverage.Partial, IntegerValue = 40, Weakened = true };
        for (var i = 1; i <= 40; i++)
            research.Values[$"Project{i:D2}"] = new MetricValue { Coverage = MetricCoverage.Partial, RealValue = i, Weakened = true };
        var cp = f.Checkpoint("cp1", null, 6_000_000, ["s1.rws"]);
        cp.Metrics[ResearchFamily.CompletedTicks] = research;
        cp.Metrics[RaidFamily.FirstDay] = MetricEntry.Of(MetricKeys.Revision(RaidFamily.FirstDay),
            new MetricValue { Coverage = MetricCoverage.Complete, RealValue = 5.25 });
        f.Ending(EndingType.Ship, 0, 3_000_000);

        var (full, _) = f.Build();
        var (capped, _) = f.Build(maxEntries: 12);

        Assert.Equal(43, full.Entries.Count);   // ending, first raid, research cap gap, 40 completions
        Assert.Equal(0, full.OmittedEntryCount);
        Assert.Equal(40, full.Entries.Count(e => e.Kind == ChronicleEntryKind.ResearchCompleted));
        Assert.Equal(60_000, full.Entries.Single(e => e.ResearchDefKey == "Project01").EndTick);
        Assert.Equal(315_000, full.Entries.Single(e => e.MilestoneKind == ChronicleMilestoneKind.FirstEnemyRaid).EndTick);

        Assert.Equal(12, capped.Entries.Count);
        Assert.Equal(31, capped.OmittedEntryCount);
        Assert.Single(capped.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.Single(capped.Entries, e => e.MilestoneKind == ChronicleMilestoneKind.FirstEnemyRaid);
        Assert.Single(capped.Entries, e => e.GapKind == ChronicleGapKind.ResearchKeyCapReached);
        Assert.Equal(
            Enumerable.Range(1, 9).Select(i => $"Project{i:D2}"),
            capped.Entries.Where(e => e.Kind == ChronicleEntryKind.ResearchCompleted).Select(e => e.ResearchDefKey));
    }

    [Fact]
    public void LongBranchedHistory_BuildsQuickly()
    {
        var f = new Fixture();
        for (var i = 0; i < 1500; i++)
        {
            var tick = (i + 1) * 6000;
            var file = $"save_{i:D4}.rws";
            var parent = i == 0 ? null : i % 250 == 0 ? $"cp-{i - 50}" : $"cp-{i - 1}";
            var lineageEntries = i >= 1200
                ? new[] { new LineageEntry(EndingType.Ship, 0, 1200 * 6000 - 10) }
                : Array.Empty<LineageEntry>();
            f.Save(file, tick, lineageEntries);
            f.Checkpoint($"cp-{i}", parent, tick, [file], (Deaths, Counter(i / 10)), (Raids, Counter(i / 3)));
        }
        f.Ending(EndingType.Ship, 0, 1200 * 6000 - 10);

        var (_, lineage) = f.Build();
        var sw = Stopwatch.StartNew();
        var (chronicle, _) = f.Build();
        sw.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Chronicle for 1,500 checkpoints (view, lineage and chronicle): {sw.ElapsedMilliseconds} ms, {chronicle.Entries.Count} entries");

        Assert.Equal(1500, lineage.Nodes.Count);
        Assert.Single(chronicle.Entries, e => e.Kind == ChronicleEntryKind.Ending);
        Assert.True(chronicle.Entries.Count(e => e.Kind == ChronicleEntryKind.VerifiedInterval) > 200);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Expected < 5000 ms for 1,500 checkpoints, took {sw.ElapsedMilliseconds} ms");
    }

    // ----- helpers -----

    private static ChronicleBranchKey KeyOf(ColonySaveLineage lineage, string checkpointId)
        => ChronicleBranchKey.Of(lineage.Nodes.Single(n => n.Kind == SaveLineageNodeKind.Checkpoint && n.CheckpointId == checkpointId));

    private static MetricValue Counter(long value, int since = 0)
        => new() { Coverage = MetricCoverage.Complete, IntegerValue = value, SinceTick = since };

    private static MetricValue Interrupted(long value, int since)
        => new() { Coverage = MetricCoverage.Partial, IntegerValue = value, SinceTick = since, Weakened = true };

    private static DateTime Utc(int tick) => Epoch.AddMinutes(tick / 6000.0);

    private sealed class Fixture
    {
        public Profile Profile { get; } = new();

        public SaveObservation Save(string file, int tick, params LineageEntry[] lineage)
        {
            var save = new SaveObservation
            {
                FileName = file,
                FileSize = 4096 + file.Length,
                LastWriteUtc = Utc(tick),
                Status = ObservationStatus.Ok,
                ContentHash = "content-" + file,
                GameVersion = Version,
                Identity = new ColonyIdentity { ModCampaignId = Campaign },
                Display = new DisplayInfo { FactionName = "Chronicle Test", WorldName = "Planet" },
                Values = new ObservedValues { TicksGame = tick },
                Lineage = [.. lineage],
            };
            Profile.SaveObservations.Add(save);
            return save;
        }

        public MetricObservation Checkpoint(string key, string? parent, int tick, string[] files,
            params (MetricDefinition Definition, MetricValue Value)[] values)
        {
            var obs = new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                Key = key,
                ParentKey = parent,
                PayloadHash = "hash-" + key,
                SaveTick = tick,
                ObservedUtc = Utc(tick),
                GameVersion = Version,
                ModSetHash = HashA,
                Identity = new ColonyIdentity { ModCampaignId = Campaign },
            };
            AddAliases(obs, files);
            foreach (var (definition, value) in values)
                obs.Metrics[definition.StoredKey] = MetricEntry.Of(definition.Revision, value);
            Profile.MetricObservations.Add(obs);
            return obs;
        }

        public MetricObservation SaveFileMetrics(string file, int tick, params (MetricDefinition Definition, MetricValue Value)[] values)
        {
            var obs = new MetricObservation
            {
                Source = MetricObservationSource.SaveFile,
                Key = "sha-" + file,
                SaveTick = tick,
                ObservedUtc = Utc(tick),
                GameVersion = Version,
                ModSetHash = HashA,
                Identity = new ColonyIdentity { ModCampaignId = Campaign },
            };
            AddAliases(obs, [file]);
            foreach (var (definition, value) in values)
                obs.Metrics[definition.StoredKey] = MetricEntry.Of(definition.Revision, value);
            Profile.MetricObservations.Add(obs);
            return obs;
        }

        public EndingEvent Ending(EndingType type, int ordinal, int tick, bool dev = false,
            EndingSource source = EndingSource.Live, int departed = 3)
        {
            var ending = new EndingEvent
            {
                CampaignId = Campaign,
                Type = type,
                Ordinal = ordinal,
                Source = source,
                TicksGame = tick,
                RealTimeUtc = Utc(tick),
                Departed = departed,
                DevMode = dev,
                GameVersion = Version,
            };
            Profile.EndingEvents.Add(ending);
            return ending;
        }

        public (ColonyChronicle Chronicle, ColonySaveLineage Lineage) Build(
            ChronicleBranchKey? branch = null, bool includeDev = false, int maxEntries = ColonyChronicle.DefaultMaxEntries)
        {
            var colonies = CampaignMatcher.BuildColonies(Profile);
            var colony = Assert.Single(colonies);
            var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
            // As the statistics window builds it: every implemented statistic is visible.
            var view = new StatsView
            {
                Colonies = colonies,
                Stats = stats,
                Numeric = MetricCalculator.BuildView(Profile, colonies, stats,
                    MetricRegistry.Compose(Profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits)),
            };
            var lineage = ColonySaveLineage.Build(Profile, colonies, colony.Key);
            var chronicle = ColonyChronicle.Build(view, colony.Key, lineage, branch, includeDev, maxEntries);
            Assert.NotNull(chronicle);
            return (chronicle!, lineage);
        }

        private void AddAliases(MetricObservation obs, string[] files)
        {
            foreach (var file in files)
            {
                var save = Profile.SaveObservations.First(s => s.FileName == file);
                obs.FileAliases.Add(new MetricFileAlias { FileName = file, FileSize = save.FileSize, LastWriteUtc = save.LastWriteUtc });
            }
        }
    }
}
