namespace RimworldStatistics.Core.Tests.Common;

/// <summary>
/// Common contract of event recording on the shared primitives every optional MOD counter uses: only a committed,
/// positive, actual amount counts; a started but unfinished action counts nothing until it completes, and then once;
/// one real event is counted once only where a real unique id proves it is the same event; two independent successes are
/// never merged; loading and re-importing add nothing. Whether an action succeeded, belongs to the player and how much it
/// really produced is each MOD rule's decision, tested with <see cref="ConditionMatrix"/> in that rule's own tests.
/// </summary>
[Trait("Category", "Contract")]
public sealed class EventRecordingContractTests
{
    private static readonly TestColony Colony = new("events");

    public static TheoryData<bool, int> Shapes() => new() { { false, 1 }, { true, 1 }, { false, 2 }, { true, 2 } };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void OnlyCommittedPositiveAmountsCountAndAMeasuredZeroIsNotUnknown(bool breakdown, int revision)
    {
        var metric = TestShapes.CounterMetric(breakdown: breakdown, revision: revision);
        var game = GameSession.NewGame(Colony, TestShapes.FamilyOf(metric));

        Assert.False(game.Record(metric, 0, TestShapes.DefKey("A")));
        Assert.False(game.Record(metric, -4, TestShapes.DefKey("A")));
        var nothing = game.Save(100, "zero");

        var value = Scalar(nothing, metric);
        Assert.Equal(0, value.IntegerValue);   // recorded from the start of the game: a measured zero
        Assert.Equal(MetricCoverage.Complete, value.Coverage);
        if (breakdown)
            Assert.Single(nothing.Observation.Metrics[metric.StableKey].Values);   // no invented per-Def zero

        // The amount is the actual quantity passed (e.g. 3 items of a stack), not one event per call.
        Assert.True(game.Record(metric, 3, TestShapes.DefKey("A")));
        var three = game.Save(200, "three");
        Assert.Equal(3, Scalar(three, metric).IntegerValue);
        if (breakdown)
            Assert.Equal(3, three.Observation.Metrics[metric.StableKey].Values[TestShapes.DefKey("A")].IntegerValue);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheStatisticOfEveryRevisionIsShownFromItsOwnFamily(bool breakdown, int revision)
    {
        // A family's outputs are read by its descriptor's definition; a revised family must not record values nobody can show.
        var metric = TestShapes.CounterMetric(breakdown: breakdown, revision: revision);
        var game = GameSession.NewGame(Colony, TestShapes.FamilyOf(metric));
        game.Record(metric, 2, TestShapes.DefKey("A"));
        var save = game.Save(100, "c1");

        Assert.Equal(revision, save.Observation.Metrics[metric.StableKey].Revision);
        var view = new TestProfile().Played(Colony, 100).Remember(metric).Import(save).Reopen().View();
        Assert.Equal(2, view.Row(Colony, metric.StableKey).Value.Number);
    }

    [Fact]
    public void TwoIndependentSuccessesAreNeverMergedIntoOne()
    {
        // Same Def, same tick, same amount: without a real id nothing says they were one event.
        var metric = TestShapes.CounterMetric(breakdown: true);
        var realMetric = TestShapes.RealMetric();
        var game = GameSession.NewGame(Colony, TestShapes.FamilyOf(metric), TestShapes.FamilyOf(realMetric));
        Assert.False(game.RecordReal(realMetric, 0));
        Assert.False(game.RecordReal(realMetric, -0.5));
        Assert.False(game.RecordReal(realMetric, double.NaN));
        Assert.True(game.Record(metric, 1, TestShapes.DefKey("A")));
        Assert.True(game.Record(metric, 1, TestShapes.DefKey("A")));
        Assert.True(game.RecordReal(realMetric, 0.35));
        Assert.True(game.RecordReal(realMetric, 0.45));

        var save = game.Save(100, "c1");

        Assert.Equal(2, Scalar(save, metric).IntegerValue);
        Assert.Equal(2, save.Observation.Metrics[metric.StableKey].Values[TestShapes.DefKey("A")].IntegerValue);
        Assert.Equal(0.8d, Scalar(save, realMetric).RealValue!.Value, 6);
    }

    [Fact]
    public void OneEventSeenByTwoHooksCountsOnceOnlyWhenItsIdProvesIt()
    {
        var api = new RimworldStatistics.Api.ApiMetricDefinition
        {
            ProviderId = "example.provider", OwnerPackageId = TestShapes.Owner, FamilyId = "visits", MetricName = "completed",
            Kind = RimworldStatistics.Api.ApiMetricKind.Event, EventMode = RimworldStatistics.Api.ApiEventMode.Count,
        };
        var state = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 100 };

        // The same visit reported by the arrival hook and again by the departure hook: one id, one count.
        Assert.Equal(RimworldStatistics.Api.ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(state, api, "visit-1", 10, 1));
        Assert.Equal(RimworldStatistics.Api.ApiRecordResult.Duplicate, ExternalSdkMetricPolicy.Record(state, api, "visit-1", 20, 1));
        // Another visit at the same tick: another id, another count.
        Assert.Equal(RimworldStatistics.Api.ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(state, api, "visit-2", 20, 1));
        Assert.Equal(RimworldStatistics.Api.ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.Record(state, api, null, 20, 1));
        Assert.Equal(RimworldStatistics.Api.ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.Record(state, api, " ", 20, 1));

        Assert.Equal(2, state.Peek("sdk.value.completed")!.N);
        // Ids are kept only as one-way digests.
        Assert.DoesNotContain(state.Peek("sdk.seen.completed")!.By!.Keys, k => k.Contains("visit", StringComparison.Ordinal));
    }

    [Fact]
    public void AStartedActionCountsNothingUntilItCompletesAndThenOnceAcrossALoad()
    {
        var metric = TestShapes.CounterMetric();
        var family = TestShapes.FamilyOf(metric);
        var game = GameSession.NewGame(Colony, family);

        // A reservation (job started) is an open interval, not an outcome.
        Assert.True(game.State(metric).Track("open", "job-1", 10));
        Assert.True(game.State(metric).Track("open", "job-2", 15));
        var reserved = game.Save(20, "reserved");
        Assert.Equal(0, Scalar(reserved, metric).IntegerValue);

        // Loaded at the save's tick: the open actions continue; one completes, one is abandoned.
        var loaded = GameSession.Load(Colony, reserved, family);
        Assert.Equal(10, loaded.State(metric).Untrack("open", "job-1"));
        Assert.True(loaded.Record(metric));
        Assert.Equal(15, loaded.State(metric).Untrack("open", "job-2"));   // abandoned: no outcome recorded
        Assert.Null(loaded.State(metric).Untrack("open", "job-1"));         // a second completion of the same job finds nothing
        var done = loaded.Save(40, "done");
        Assert.Equal(1, Scalar(done, metric).IntegerValue);

        // After a gap (an event was missed), an action started before the gap cannot complete into the total.
        var again = GameSession.Load(Colony, done, family);
        again.State(metric).Track("open", "job-3", 45);
        again.Accumulator.MarkGap(metric.AcquisitionId, 50);
        Assert.Null(again.State(metric).Untrack("open", "job-3"));
        var afterGap = again.Save(60, "gap");
        Assert.Equal(1, Scalar(afterGap, metric).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, Scalar(afterGap, metric).Coverage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingASaveOrImportingItAgainAddsNothing(bool breakdown)
    {
        var metric = TestShapes.CounterMetric(breakdown: breakdown);
        var family = TestShapes.FamilyOf(metric);
        var game = GameSession.NewGame(Colony, family);
        game.Record(metric, 4, TestShapes.DefKey("A"));
        var saved = game.Save(100, "c1");

        // The game makes the loaded checkpoint's observation again on load (to recover an unconfirmed save).
        var loaded = GameSession.Load(Colony, saved, family);
        var onLoad = CheckpointMetrics.ToObservation(loaded.Accumulator.Loaded!, loaded.Accumulator.LoadedJson!,
            Colony.Identity, "1.7.4985", null, [family]);
        var resaved = loaded.Save(100, "c2");   // saved again without playing

        var profile = new TestProfile().Played(Colony, 100).Remember(metric)
            .Import(saved.Observation, onLoad, saved.Observation).Import(resaved).Import(resaved);
        var before = ProfileJson.Serialize(profile.Profile);
        profile.Import(saved, resaved);   // the same files read again

        Assert.Equal(before, ProfileJson.Serialize(profile.Profile));
        Assert.Equal(2, profile.Profile.MetricObservations.Count(o => o.Source == MetricObservationSource.Checkpoint));
        Assert.Equal(4, Scalar(resaved, metric).IntegerValue);
        Assert.Equal(4, profile.View().Row(Colony, metric.StableKey).Value.Number);
    }

    [Fact]
    public void ConditionMatrixReportsEveryCombinationARuleGetsWrong()
    {
        // The helper the MOD rule tests use: a rule that forgot its player check is caught with the exact row.
        static bool CountsWithoutPlayerCheck(bool[] c) => c[0] && c[2];   // succeeded, (player ignored), positive amount
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ConditionMatrix.AssertOnlyWhenAll(3, CountsWithoutPlayerCheck));
        Assert.Contains("(T, F, T) → counted", error.Message, StringComparison.Ordinal);
        ConditionMatrix.AssertOnlyWhenAll(3, c => c[0] && c[1] && c[2]);
        Assert.Equal(8, ConditionMatrix.All(3).Select(c => string.Concat(c.Select(b => b ? 1 : 0))).Distinct().Count());
    }

    private static MetricValue Scalar(SavedCheckpoint save, ModMetricDescriptor metric)
        => save.Observation.Metrics[metric.StableKey].Values[MetricEntry.Scalar];
}
