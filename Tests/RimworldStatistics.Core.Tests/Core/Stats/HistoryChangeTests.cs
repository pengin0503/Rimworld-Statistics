using System.Text.Json;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Issue #307: the change between two readings of a colony's history is shown only for a reading the later one is proven
/// to continue, and a running total is counted only over an interval it was recorded in without a gap.
/// </summary>
public sealed class HistoryChangeTests
{
    private const string Campaign = "one";
    private static readonly MetricDefinition SaveMetric = MetricRegistry.Find(MetricKeys.WealthItemsPeak)!;
    private static readonly MetricDefinition RunningTotal = MetricRegistry.Find(ResearchFamily.Completed)!;
    private static readonly DateTime Epoch = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheStatisticsUsed_AreAStateAndARunningTotal()
    {
        Assert.False(HistoryChanges.IsRunningTotal(SaveMetric));
        Assert.True(HistoryChanges.IsRunningTotal(RunningTotal));
        Assert.True(HistoryChanges.IsRunningTotal(BasicMetrics.Definition(BasicMetrics.Raids)));
        Assert.False(HistoryChanges.IsRunningTotal(BasicMetrics.Definition(BasicMetrics.LastWealth)));
        // Sizes of sets and signed changes are not counted as totals of the interval.
        Assert.False(HistoryChanges.IsRunningTotal(MetricRegistry.Find(WorkFamily.SownDefs)!));
        Assert.False(HistoryChanges.IsRunningTotal(MetricRegistry.Find(MetricKeys.WealthTotalNet)!));
    }

    [Fact]
    public void ReadingsOnOneLineOfSaves_ChangeByTheirDifferenceInEitherOrder()
    {
        var history = History(Profile(
            Save("a.rws", 60000, 100), CheckpointIn("a.rws", "a", null, 60000),
            Save("b.rws", 120000, 150), CheckpointIn("b.rws", "b", "a", 120000),
            Save("c.rws", 180000, 150), CheckpointIn("c.rws", "c", "b", 180000),
            Save("d.rws", 300000, 120), CheckpointIn("d.rws", "d", "c", 300000)));

        Assert.Equal((50d, 0.5), Shown(HistoryChanges.Compare(SaveMetric, history, 0, 1)));
        Assert.Equal((0d, 0d), Shown(HistoryChanges.Compare(SaveMetric, history, 1, 2)));
        Assert.Equal((-30d, -0.2), Shown(HistoryChanges.Compare(SaveMetric, history, 2, 3)));
        var whole = HistoryChanges.Compare(SaveMetric, history, 3, 0);   // picked later reading first
        Assert.Equal((0, 3, 240000L, HistoryChangeKind.Difference), (whole.FromIndex, whole.ToIndex, whole.Ticks, whole.Kind));
        Assert.Equal((20d, 0.2), Shown(whole));
        Assert.Equal((0, 3), HistoryChanges.FirstAndLast(history));
    }

    [Fact]
    public void ABranchContinuesItsOwnAncestorsOnly_NeverASiblingOrAReloadedTimeline()
    {
        // a ─┬─ b2(20) ── c2(35)        a reload of "a" after b1 was played:
        //    └─ b1(30) ── c1(40)        sorted by tick the branches interleave.
        var history = History(Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b2.rws", 20, 120), CheckpointIn("b2.rws", "b2", "a", 20),
            Save("b1.rws", 30, 300), CheckpointIn("b1.rws", "b1", "a", 30),
            Save("c2.rws", 35, 130), CheckpointIn("c2.rws", "c2", "b2", 35),
            Save("c1.rws", 40, 310), CheckpointIn("c1.rws", "c1", "b1", 40)));

        Assert.Equal(210d, HistoryChanges.Compare(SaveMetric, history, 0, 4).Delta);   // c1 continues a through b1
        Assert.Equal(10d, HistoryChanges.Compare(SaveMetric, history, 2, 4).Delta);
        foreach (var (a, b) in new[] { (1, 2), (1, 4), (3, 4), (2, 3) })
        {
            var change = HistoryChanges.Compare(SaveMetric, history, a, b);
            Assert.Equal(HistoryChangeIssue.NotLinked, change.Issue);
            Assert.Null(change.Delta);
            Assert.Null(change.Ratio);
        }
        Assert.Equal((0, 4), HistoryChanges.FirstAndLast(history));
    }

    [Fact]
    public void SavesWithoutCheckpointsOrAfterAMissingParent_AreNotCompared()
    {
        var unproven = History(Profile(Save("a.rws", 10, 100), Save("b.rws", 20, 120), Save("c.rws", 30, 140)));
        Assert.Equal(HistoryChangeIssue.NotLinked, HistoryChanges.Compare(SaveMetric, unproven, 0, 1).Issue);
        // "First and last" then names the first and last readings, and says why they cannot be compared.
        Assert.Equal((0, 2), HistoryChanges.FirstAndLast(unproven));
        Assert.Equal(HistoryChangeIssue.NotLinked, HistoryChanges.Compare(SaveMetric, unproven, 0, 2).Issue);

        var missingParent = History(Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("c.rws", 30, 140), CheckpointIn("c.rws", "c", "b-was-deleted", 30)));
        Assert.Equal(HistoryChangeIssue.NotLinked, HistoryChanges.Compare(SaveMetric, missingParent, 0, 1).Issue);
    }

    [Fact]
    public void ACheckpointCarriedByLaterSaves_IsOneReadingCountedAtItsOwnTick()
    {
        // The supplied 21:07/21:19 shape: two later saves rewrote checkpoint e (tick 40) unchanged.
        var profile = Profile(
            Save("d.rws", 30, 100), CheckpointIn("d.rws", "d", null, 30),
            Save("late1.rws", 50, 140), Save("late2.rws", 60, 150),
            CheckpointIn(["late1.rws", "late2.rws"], "e", "d", 40));

        // The saves both continue d, but neither continues the other: no change between them.
        var saves = History(profile);
        Assert.Equal(HistoryChangeIssue.NotLinked, HistoryChanges.Compare(SaveMetric, saves, 1, 2).Issue);
        Assert.Equal(50d, HistoryChanges.Compare(SaveMetric, saves, 0, 2).Delta);

        // The checkpoint statistic: d and e, the later saves adding no reading; the interval ends at e's own tick.
        var checkpoint = History(profile, RunningTotal);
        Assert.Equal(new[] { 30, 40 }, checkpoint.Points.Select(p => p.Tick));
        var change = HistoryChanges.Compare(RunningTotal, checkpoint, 0, 1);
        Assert.Equal((HistoryChangeKind.Increase, HistoryChangeIssue.None, 10d, 10L, 60),
            (change.Kind, change.Issue, change.Delta, change.Ticks, change.To.SavedTick));
    }

    [Fact]
    public void ARunningTotal_IsCountedOnlyOverAnIntervalRecordedWithoutAGap()
    {
        var history = History(Profile(
            Checkpoint("a", null, 100, 5, MetricCoverage.Complete, since: 0),
            Checkpoint("b", "a", 200, 8, MetricCoverage.Complete, since: 0),
            Checkpoint("c", "b", 300, 9, MetricCoverage.Partial, since: 0),        // recording had a gap
            Checkpoint("d", "c", 400, 2, MetricCoverage.Complete, since: 350),     // recording started again
            Checkpoint("e", "d", 500, 1, MetricCoverage.Complete, since: 350)),    // lower than before
            RunningTotal);
        Assert.Equal(new[] { 100, 200, 300, 400, 500 }, history.Points.Select(p => p.Tick));

        var counted = HistoryChanges.Compare(RunningTotal, history, 0, 1);
        Assert.Equal((HistoryChangeKind.Increase, 3d, 0.6, false), (counted.Kind, counted.Delta, counted.Ratio, counted.Partial));
        foreach (var (a, b) in new[] { (1, 2), (0, 2), (2, 3), (1, 3) })
            Assert.Equal(HistoryChangeIssue.CounterGap, HistoryChanges.Compare(RunningTotal, history, a, b).Issue);
        var lower = HistoryChanges.Compare(RunningTotal, history, 3, 4);
        Assert.Equal((HistoryChangeIssue.CounterDecrease, (double?)null), (lower.Issue, lower.Delta));

        // The game's counters in a save run from the game's start: a proven line counts them, a lower one is refused.
        var raids = BasicMetrics.Definition(BasicMetrics.Raids);
        var saves = Linked(MetricHistoryKind.SaveObservations, 3, 5, 2);
        Assert.Equal((HistoryChangeKind.Increase, 2d), (HistoryChanges.Compare(raids, saves, 0, 1).Kind, HistoryChanges.Compare(raids, saves, 0, 1).Delta));
        Assert.Equal(HistoryChangeIssue.CounterDecrease, HistoryChanges.Compare(raids, saves, 1, 2).Issue);
    }

    [Fact]
    public void ATotalRecordedFromALaterStartWithoutAGap_IsCountedAndSaysWhenItsRecordingBegan()
    {
        // The MOD was added on a running game: every value is partial because recording began at tick 1000, not by a gap.
        var acc = LiveMetricAccumulator.StartNew([Counter.Spec], 1000);
        Events(acc, 3);
        var a = Commit(acc, "a", 2000);
        Events(acc, 4);
        var b = Commit(acc, "b", 3000);
        acc.MarkGap(Counter.Name, 3500);   // an event was missed
        Events(acc, 2);
        var c = Commit(acc, "c", 4000);

        Assert.Equal(["test.307"], a.ContinuousFamilies!);
        Assert.Empty(c.ContinuousFamilies!);
        var shown = a.Scalar(CounterKey, 1)!;
        Assert.Equal((MetricCoverage.Partial, (bool?)null, 1000, 2000), (shown.Coverage, shown.Weakened, shown.SinceTick, shown.ThroughTick));

        var history = History(Profile(a, b, c), CounterDef);
        var counted = HistoryChanges.Compare(CounterDef, history, 0, 1);
        Assert.Equal((HistoryChangeKind.Increase, HistoryChangeIssue.None, 4d, 4 / 3d, false, (int?)1000),
            (counted.Kind, counted.Issue, counted.Delta, counted.Ratio, counted.Partial, counted.RecordedSince));
        foreach (var (from, to) in new[] { (1, 2), (0, 2) })
            Assert.Equal(HistoryChangeIssue.CounterGap, HistoryChanges.Compare(CounterDef, history, from, to).Issue);
        Assert.Equal((0, 2), HistoryChanges.FirstAndLast(history));

        // A total recorded from the game's start says nothing about a later start.
        var fromStart = LiveMetricAccumulator.StartNew([Counter.Spec], 0);
        Events(fromStart, 1);
        var s1 = Commit(fromStart, "s1", 2000);
        Events(fromStart, 2);
        var s2 = Commit(fromStart, "s2", 3000);
        var complete = HistoryChanges.Compare(CounterDef, History(Profile(s1, s2), CounterDef), 0, 1);
        Assert.Equal((2d, (int?)null), (complete.Delta, complete.RecordedSince));
    }

    [Fact]
    public void AWeakenedValueAFamilyStoppedBeforeTheSaveOrAnOlderRecord_IsNotCounted()
    {
        // A value its family made partial (a cap reached) is a lower bound, whatever its range.
        var capped = LiveMetricAccumulator.StartNew([Counter.Spec], 1000);
        Events(capped, 3);
        var a = Commit(capped, "a", 2000);
        capped.Family(Counter.Name)!.Count("capped");
        Events(capped, 4);
        var b = Commit(capped, "b", 3000);
        Assert.Equal(["test.307"], b.ContinuousFamilies!);
        Assert.True(b.Scalar(CounterKey, 1)!.Weakened);
        Assert.Equal(HistoryChangeIssue.CounterGap, HistoryChanges.Compare(CounterDef, History(Profile(a, b), CounterDef), 0, 1).Issue);

        // A family that does not record in a later session is kept as it was, still without a gap up to where it stopped:
        // its range ends before the later save.
        var first = LiveMetricAccumulator.StartNew([Counter.Spec], 1000);
        Events(first, 3);
        var k1 = Commit(first, "k1", 2000);
        var later = LiveMetricAccumulator.Resume(first.LoadedJson, [new FamilySpec("other", 1)], 2000);
        var k2 = Commit(later, "k2", 3000);
        Assert.Equal((MetricCoverage.Partial, 2000), (k2.Scalar(CounterKey, 1)!.Coverage, k2.Scalar(CounterKey, 1)!.ThroughTick));
        Assert.DoesNotContain("test.307", k2.ContinuousFamilies!);
        Assert.Equal(HistoryChangeIssue.CounterGap, HistoryChanges.Compare(CounterDef, History(Profile(k1, k2), CounterDef), 0, 1).Issue);

        // A checkpoint read before gaps were kept: its partial values cannot be told from weakened ones.
        var late = LiveMetricAccumulator.StartNew([Counter.Spec], 1000);
        Events(late, 3);
        var o1 = Older(Commit(late, "o1", 2000));
        Events(late, 4);
        var o2 = Older(Commit(late, "o2", 3000));
        Assert.Null(o1.ContinuousFamilies);
        Assert.Equal(HistoryChangeIssue.CounterGap, HistoryChanges.Compare(CounterDef, History(Profile(o1, o2), CounterDef), 0, 1).Issue);
    }

    [Fact]
    public void ReReadingACheckpoint_NeverProvesMoreThanItsValuesSay()
    {
        var acc = LiveMetricAccumulator.StartNew([Counter.Spec], 1000);
        Events(acc, 3);
        var aDraft = Draft(acc, "a", 2000);
        Events(acc, 4);
        var bDraft = Draft(acc, "b", 3000);
        MetricObservation Read(SaveDraft draft, IReadOnlyList<LiveFamily> families) => CheckpointMetrics.ToObservation(
            draft.Payload!, draft.Json, new ColonyIdentity { ModCampaignId = Campaign }, "1.7", null, families);
        Profile Merged(MetricObservation first, MetricObservation again)
        {
            var profile = new Profile();
            ProfileMutations.UpsertMetricObservation(profile, Read(aDraft, [Counter]));
            ProfileMutations.UpsertMetricObservation(profile, first);
            ProfileMutations.UpsertMetricObservation(profile, again);
            Assert.Equal(2, profile.MetricObservations.Count);
            return profile;
        }
        HistoryChangeIssue Issue(Profile profile) => HistoryChanges.Compare(CounterDef, History(profile, CounterDef), 0, 1).Issue;

        // Read first without the family (an optional MOD not loaded), then with it: the lists add up.
        var withoutFamily = Read(bDraft, []);
        Assert.Empty(withoutFamily.ContinuousFamilies!);
        var both = Merged(withoutFamily, Read(bDraft, [Counter]));
        Assert.Equal(["test.307"], both.MetricObservations[1].ContinuousFamilies!);
        Assert.Equal(HistoryChangeIssue.None, Issue(both));

        // A value taken from an older record of the same checkpoint may be weakened without saying so: the list goes.
        var tookOlder = Merged(Read(bDraft, []), Older(Read(bDraft, [Counter])));
        Assert.Null(tookOlder.MetricObservations[1].ContinuousFamilies);
        Assert.Equal(HistoryChangeIssue.CounterGap, Issue(tookOlder));

        // An older record read again by a reader that marks weakenings (an upgrade re-reads every save): the marked reading
        // holds every value the record holds, so it replaces them and the record is what a fresh import gives (#388).
        var older = Merged(Older(Read(bDraft, [Counter])), Read(bDraft, [Counter]));
        Assert.Equal(["test.307"], older.MetricObservations[1].ContinuousFamilies!);
        Assert.Equal(HistoryChangeIssue.None, Issue(older));
        var fresh = Merged(Read(bDraft, [Counter]), Read(bDraft, [Counter]));
        Assert.Equal(JsonSerializer.Serialize(fresh.MetricObservations[1], ProfileJson.CompactOptions),
            JsonSerializer.Serialize(older.MetricObservations[1], ProfileJson.CompactOptions));

        // A value only the older record holds (its optional family is not loaded now) may be weakened without saying so:
        // the record takes the new values as before and stays without a list.
        var otherFamily = new LiveFamily { Name = "test.307.other", Revision = 1, Build = c => c.Put("test.307.other.count", c.Value(integer: 1)) };
        var withOther = LiveMetricAccumulator.StartNew([Counter.Spec, otherFamily.Spec], 1000);
        Events(withOther, 3);
        var oa = Draft(withOther, "oa", 2000);
        Events(withOther, 4);
        var ob = Draft(withOther, "ob", 3000);
        var partialReRead = new Profile();
        ProfileMutations.UpsertMetricObservation(partialReRead, Read(oa, [Counter, otherFamily]));
        ProfileMutations.UpsertMetricObservation(partialReRead, Older(Read(ob, [Counter, otherFamily])));
        ProfileMutations.UpsertMetricObservation(partialReRead, Read(ob, [Counter]));
        Assert.Null(partialReRead.MetricObservations[1].ContinuousFamilies);
        Assert.True(partialReRead.MetricObservations[1].Metrics.ContainsKey("test.307.other.count"));
        Assert.Equal(HistoryChangeIssue.CounterGap, Issue(partialReRead));

        // A marked record of other content (another payload hash) never lends its list.
        var target = Older(Read(bDraft, [Counter]));
        var otherContent = Read(bDraft, [Counter]);
        otherContent.PayloadHash = "other";
        ProfileMutations.RestoreArchivedMetrics(target, otherContent);
        Assert.Null(target.ContinuousFamilies);
    }

    [Fact]
    public void AnUpgradedRecord_KeepsTheValuesItsFamilyWeakened_AndTheListAfterAnArchiveRestore()
    {
        // The re-read gives the list, never more than its values say: a capped total stays a lower bound.
        var capped = LiveMetricAccumulator.StartNew([Counter.Spec], 1000);
        Events(capped, 3);
        var aDraft = Draft(capped, "a", 2000);
        capped.Family(Counter.Name)!.Count("capped");
        Events(capped, 4);
        var bDraft = Draft(capped, "b", 3000);
        MetricObservation Read(SaveDraft draft) => CheckpointMetrics.ToObservation(
            draft.Payload!, draft.Json, new ColonyIdentity { ModCampaignId = Campaign }, "1.7", null, [Counter]);
        var profile = new Profile();
        ProfileMutations.UpsertMetricObservation(profile, Older(Read(aDraft)));
        ProfileMutations.UpsertMetricObservation(profile, Older(Read(bDraft)));
        ProfileMutations.UpsertMetricObservation(profile, Read(aDraft));
        ProfileMutations.UpsertMetricObservation(profile, Read(bDraft));
        Assert.All(profile.MetricObservations, o => Assert.Equal(["test.307"], o.ContinuousFamilies!));
        Assert.True(profile.MetricObservations[1].Scalar(CounterKey, 1)!.Weakened);
        Assert.Equal(HistoryChangeIssue.CounterGap,
            HistoryChanges.Compare(CounterDef, History(profile, CounterDef), 0, 1).Issue);

        // An archive keeps the older copy first and the marked copy after it: restoring both in that order ends marked.
        var restored = Older(Read(aDraft));
        restored.Metrics.Clear();
        restored.ContinuousFamilies = ["test.307"];
        ProfileMutations.RestoreArchivedMetrics(restored, Older(Read(aDraft)));
        Assert.Null(restored.ContinuousFamilies);
        ProfileMutations.RestoreArchivedMetrics(restored, Read(aDraft));
        Assert.Equal(["test.307"], restored.ContinuousFamilies!);
    }

    [Fact]
    public void TheMarksSurviveTheProfileFile_AndAreWrittenOnlyWhenTheyHold()
    {
        var acc = LiveMetricAccumulator.StartNew([Counter.Spec], 1000);
        Events(acc, 3);
        var a = Commit(acc, "a", 2000);
        acc.Family(Counter.Name)!.Count("capped");
        var b = Commit(acc, "b", 3000);
        var profile = ProfileJson.Deserialize(ProfileJson.Serialize(Profile(a, b)))!;
        Assert.Equal(["test.307"], profile.MetricObservations[0].ContinuousFamilies!);
        Assert.Null(profile.MetricObservations[0].Scalar(CounterKey, 1)!.Weakened);
        Assert.True(profile.MetricObservations[1].Scalar(CounterKey, 1)!.Weakened);
        Assert.DoesNotContain("\"weakened\"", JsonSerializer.Serialize(a, ProfileJson.CompactOptions));
        Assert.Contains("\"weakened\":true", JsonSerializer.Serialize(b, ProfileJson.CompactOptions));
        Assert.DoesNotContain("continuousFamilies", JsonSerializer.Serialize(Older(a), ProfileJson.CompactOptions));
    }

    [Fact]
    public void APartialStateReading_IsComparedAsTheValuesShown()
    {
        var history = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new(10, new NumericValue { Number = 100, Coverage = MetricCoverage.SnapshotOnly }),
            new(20, new NumericValue { Number = 80, Coverage = MetricCoverage.Partial }, ConnectToPrevious: true),
        ]);
        var change = HistoryChanges.Compare(SaveMetric, history, 0, 1);
        Assert.Equal((HistoryChangeIssue.None, -20d, -0.2, true), (change.Issue, change.Delta, change.Ratio, change.Partial));
    }

    [Fact]
    public void GapsUnknownValuesOneReadingAndOneTick_GiveNoChange()
    {
        var history = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new(10, new NumericValue { Number = 100, Coverage = MetricCoverage.SnapshotOnly }),
            new(20, null, ConnectToPrevious: true),
            new(30, NumericValue.Unknown, ConnectToPrevious: true),
            new(40, new NumericValue { Number = double.NaN, Coverage = MetricCoverage.SnapshotOnly }, ConnectToPrevious: true),
            new(50, new NumericValue { Number = 130, Coverage = MetricCoverage.SnapshotOnly }, ConnectToPrevious: true),
        ]);
        foreach (var gap in new[] { 1, 2, 3 })
            Assert.Equal(HistoryChangeIssue.NoValue, HistoryChanges.Compare(SaveMetric, history, 0, gap).Issue);
        Assert.Equal(HistoryChangeIssue.SamePoint, HistoryChanges.Compare(SaveMetric, history, 4, 4).Issue);
        // A line of saves passes through readings without a value.
        Assert.Equal(30d, HistoryChanges.Compare(SaveMetric, history, 0, 4).Delta);
        Assert.Equal((0, 4), HistoryChanges.FirstAndLast(history));

        var one = new MetricHistory(MetricHistoryKind.SaveObservations,
            [new(10, new NumericValue { Number = 100, Coverage = MetricCoverage.SnapshotOnly }), new(20, null)]);
        Assert.Null(HistoryChanges.FirstAndLast(one));
        Assert.Null(HistoryChanges.FirstAndLast(new MetricHistory(MetricHistoryKind.SaveObservations, [])));
    }

    [Fact]
    public void ARecorderIsOneSavesOwnPast_EvenAcrossAnInvalidReading()
    {
        var points = HistoryMetricReader.RecorderHistory(HistoryMetricReader.ColonistMood, [55f, 0f, 70f, 75f], "1.7", 90000);
        var history = new MetricHistory(MetricHistoryKind.GameRecorder, points);
        var mood = MetricRegistry.Find(MetricKeys.MoodMean)!;
        Assert.False(points[2].ConnectToPrevious);
        Assert.Equal((HistoryChangeKind.Difference, 20d), (HistoryChanges.Compare(mood, history, 0, 3).Kind, HistoryChanges.Compare(mood, history, 0, 3).Delta));
        Assert.Equal(HistoryChangeIssue.NoValue, HistoryChanges.Compare(mood, history, 0, 1).Issue);
        Assert.Equal((0, 3), HistoryChanges.FirstAndLast(history));
    }

    [Fact]
    public void ARatioIsNeverInvented_ForAZeroBaseACelsiusScaleOrNumbersTooLarge()
    {
        Assert.Equal((5d, (double?)null, HistoryRatioIssue.ZeroBase), Ratio(SaveMetric, 0, 5));
        Assert.Equal((0d, (double?)null, HistoryRatioIssue.ZeroBase), Ratio(SaveMetric, 0, 0));
        var celsius = MetricRegistry.ImplementedCatalog.First(d => d.Unit == MetricUnit.Celsius);
        Assert.Equal((-15d, (double?)null, HistoryRatioIssue.Unit), Ratio(celsius, 10, -5));
        Assert.Equal((75d, 1.5, HistoryRatioIssue.None), Ratio(SaveMetric, -50, 25));   // relative to the magnitude
        Assert.Equal((1e300, (double?)null, HistoryRatioIssue.Overflow), Ratio(SaveMetric, 1e-300, 1e300));
        Assert.Equal((2e12, 2e12 / 1e12, HistoryRatioIssue.None), Ratio(SaveMetric, 1e12, 3e12));

        var overflow = HistoryChanges.Compare(SaveMetric, Linked(MetricHistoryKind.GameRecorder, double.MaxValue, -double.MaxValue), 0, 1);
        Assert.Equal((HistoryChangeIssue.Overflow, (double?)null), (overflow.Issue, overflow.Delta));
    }

    private const string CounterKey = "test.307.count";

    /// <summary>Counts "events"; its total is weakened once anything was "capped".</summary>
    private static readonly LiveFamily Counter = new()
    {
        Name = "test.307",
        Revision = 1,
        Build = c => c.Put(CounterKey, c.Value(integer: c.State.Peek("events")?.N ?? 0, partial: c.State.Peek("capped") is not null)),
    };

    private static readonly MetricDefinition CounterDef = new()
    {
        Key = CounterKey, StoredKey = CounterKey, LedgerIds = "T-307", Category = MetricCategory.Colony, Unit = MetricUnit.Count,
        Provenance = MetricProvenance.LiveAggregate, Source = MetricObservationSource.Checkpoint, Overall = OverallAggregation.Sum,
        Acquisition = Counter.Name, LabelKey = "x", DescriptionKey = "x",
    };

    private static void Events(LiveMetricAccumulator acc, int n)
    {
        for (var i = 0; i < n; i++)
            acc.Family(Counter.Name)!.Count("events");
    }

    private static SaveDraft Draft(LiveMetricAccumulator acc, string id, int tick)
    {
        var draft = acc.PrepareSave(tick, Epoch, id)!;
        acc.Commit(draft);
        return draft;
    }

    private static MetricObservation Commit(LiveMetricAccumulator acc, string id, int tick)
    {
        var draft = Draft(acc, id, tick);
        return CheckpointMetrics.ToObservation(draft.Payload!, draft.Json, new ColonyIdentity { ModCampaignId = Campaign }, "1.7",
            null, [Counter]);
    }

    /// <summary>The same checkpoint as an earlier version recorded it: no list of continuous families, no weakening marks.</summary>
    private static MetricObservation Older(MetricObservation observation)
    {
        var json = JsonSerializer.Serialize(observation, ProfileJson.CompactOptions);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove("continuousFamilies");
        foreach (var (_, entry) in node["metrics"]!.AsObject())
            foreach (var (_, value) in entry!["values"]!.AsObject())
                value!.AsObject().Remove("weakened");
        return JsonSerializer.Deserialize<MetricObservation>(node.ToJsonString(), ProfileJson.CompactOptions)!;
    }

    private static (double?, double?, HistoryRatioIssue) Ratio(MetricDefinition definition, double from, double to)
    {
        var change = HistoryChanges.Compare(definition, Linked(MetricHistoryKind.GameRecorder, from, to), 0, 1);
        Assert.Equal(HistoryChangeIssue.None, change.Issue);
        return (change.Delta, change.Ratio, change.RatioIssue);
    }

    private static (double?, double?) Shown(HistoryChange change)
    {
        Assert.Equal(HistoryChangeIssue.None, change.Issue);
        return (change.Delta, change.Ratio);
    }

    /// <summary>Readings one day apart, each continuing the one before.</summary>
    private static MetricHistory Linked(MetricHistoryKind kind, params double[] values) => new(kind,
        values.Select((v, i) => new MetricHistoryPoint((i + 1) * StatFormat.TicksPerDay,
            new NumericValue { Number = v, Coverage = MetricCoverage.SnapshotOnly }, ConnectToPrevious: i > 0)).ToArray());

    private static MetricHistory History(Profile profile, MetricDefinition? definition = null)
    {
        var colony = new Colony { Key = "id:" + Campaign };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = Campaign });
        return MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, definition ?? SaveMetric);
    }

    private static Profile Profile(params MetricObservation[] observations) => new() { MetricObservations = [.. observations] };

    private static MetricFileAlias Alias(string file) => new()
    {
        FileName = file, FileSize = 1000 + file.Length, LastWriteUtc = Epoch.AddSeconds(file.Sum(c => c)),
    };

    private static MetricObservation Save(string file, int tick, int? wealth)
    {
        var observation = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "sha-" + file, SaveTick = tick,
            Identity = new ColonyIdentity { ModCampaignId = Campaign },
            FileAliases = { Alias(file) },
        };
        if (wealth is { } number)
            observation.Metrics[SaveMetric.StoredKey] = MetricEntry.Of(SaveMetric.Revision,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = number });
        return observation;
    }

    private static MetricObservation Checkpoint(string key, string? parent, int tick, int? value = null,
        MetricCoverage coverage = MetricCoverage.Complete, int? since = 0) => new()
    {
        Source = MetricObservationSource.Checkpoint, Key = key, ParentKey = parent, SaveTick = tick,
        PayloadHash = key, Identity = new ColonyIdentity { ModCampaignId = Campaign },
        Metrics = { [RunningTotal.StoredKey] = MetricEntry.Of(RunningTotal.Revision,
            new MetricValue { Coverage = coverage, IntegerValue = value ?? tick, SinceTick = since }) },
    };

    private static MetricObservation CheckpointIn(string file, string key, string? parent, int tick)
        => CheckpointIn([file], key, parent, tick);

    private static MetricObservation CheckpointIn(string[] files, string key, string? parent, int tick)
    {
        var checkpoint = Checkpoint(key, parent, tick);
        checkpoint.FileAliases.AddRange(files.Select(Alias));
        return checkpoint;
    }
}
