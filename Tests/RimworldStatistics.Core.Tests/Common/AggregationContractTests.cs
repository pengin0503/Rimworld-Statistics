namespace RimworldStatistics.Core.Tests.Common;

/// <summary>
/// Common contract of aggregation for optional MOD statistics: lifetime totals and states at a save, minimum and maximum
/// over a colony's saves and across colonies, per-Def breakdowns, partial and unknown values, and no double counting.
/// </summary>
[Trait("Category", "Contract")]
public sealed class AggregationContractTests
{
    public static TheoryData<OverallAggregation, MetricObservationSource> Aggregations()
    {
        var data = new TheoryData<OverallAggregation, MetricObservationSource>();
        foreach (var overall in new[] { OverallAggregation.Sum, OverallAggregation.Max, OverallAggregation.Min })
            foreach (var source in new[] { MetricObservationSource.Checkpoint, MetricObservationSource.SaveSnapshot })
                data.Add(overall, source);
        return data;
    }

    [Theory]
    [MemberData(nameof(Aggregations))]
    public void ColoniesCombineByTheirOwnLatestValueAndAnUnknownColonyIsNeverZero(OverallAggregation overall, MetricObservationSource source)
    {
        var metric = TestShapes.CounterMetric(overall: overall, source: source, breakdown: true);
        var (a, b, unknown, excluded) = (new TestColony("a"), new TestColony("b"), new TestColony("unknown"), new TestColony("excluded"));
        var profile = new TestProfile().Played(a, 200).Played(b, 100).Played(unknown, 100).Played(excluded, 100).Remember(metric)
            .Import(Reading(a, metric, "a1", 100, (TestShapes.DefKey("X"), 3)),
                Reading(a, metric, "a2", 200, (TestShapes.DefKey("X"), 3), (TestShapes.DefKey("Y"), 2)),
                Reading(b, metric, "b1", 100, (TestShapes.DefKey("X"), 2)),
                Reading(excluded, metric, "e1", 100, (TestShapes.DefKey("X"), 100)))
            .Exclude(excluded);

        var view = profile.View();
        Assert.Equal(5, view.Row(a, metric.StableKey).Value.Number);   // the latest save, not 3 + 5
        Assert.Equal(MetricCoverage.Unknown, view.Row(unknown, metric.StableKey).Value.Coverage);

        var total = view.Overall(metric.StableKey);
        Assert.Equal(overall switch { OverallAggregation.Sum => 7, OverallAggregation.Max => 5, _ => 2 }, total.Value.Number);
        Assert.Equal(MetricCoverage.Partial, total.Value.Coverage);   // one colony could not be read
        Assert.Equal(3, total.Targets);                               // the excluded colony is not a target
        Assert.Equal(2, total.Valid);
        if (overall == OverallAggregation.Sum)
        {
            Assert.Equal(new[] { TestShapes.DefKey("X"), TestShapes.DefKey("Y") }, total.Breakdowns.Select(r => r.Breakdown));
            Assert.Equal(new double?[] { 5, 2 }, total.Breakdowns.Select(r => r.Value.Number));
        }
        else
        {
            Assert.Equal(overall == OverallAggregation.Max ? view.Colony(a).Key : view.Colony(b).Key, total.Value.ColonyKey);
        }
    }

    [Theory]
    [InlineData(MetricObservationSource.Checkpoint)]
    [InlineData(MetricObservationSource.SaveSnapshot)]
    public void APartialColonyMakesTheTotalPartialAndCompleteColoniesKeepItComplete(MetricObservationSource source)
    {
        var metric = TestShapes.CounterMetric(source: source);
        var (a, b) = (new TestColony("a"), new TestColony("b"));
        TestProfile Profile(MetricCoverage coverageOfB) => new TestProfile().Played(a, 100).Played(b, 100).Remember(metric)
            .Import(Reading(a, metric, "a1", 100), Reading(b, metric, "b1", 100, amount: 3, coverage: coverageOfB));

        var measured = source == MetricObservationSource.Checkpoint ? MetricCoverage.Complete : MetricCoverage.SnapshotOnly;
        Assert.Equal(measured, Profile(measured).View().Overall(metric.StableKey).Value.Coverage);
        var partial = Profile(MetricCoverage.Partial).View().Overall(metric.StableKey).Value;
        Assert.Equal(MetricCoverage.Partial, partial.Coverage);
        Assert.Equal(6, partial.Number);
    }

    [Theory]
    [InlineData(ColonySelection.Representative, 6)]
    [InlineData(ColonySelection.ObservationMax, 9)]
    [InlineData(ColonySelection.ObservationMin, 3)]
    public void AStateAtASaveIsTheLatestSaveAndItsExtremesSpanEverySave(ColonySelection selection, int expected)
    {
        var metric = TestShapes.CounterMetric(source: MetricObservationSource.SaveSnapshot, selection: selection,
            overall: OverallAggregation.Max);
        var colony = new TestColony("states");
        var profile = new TestProfile().Played(colony, 300).Remember(metric)
            .Import(Reading(colony, metric, "s1", 100, amount: 3), Reading(colony, metric, "s2", 200, amount: 9),
                Reading(colony, metric, "s3", 300, amount: 6));

        var value = profile.View().Row(colony, metric.StableKey).Value;
        Assert.Equal(expected, value.Number);
        Assert.Equal(MetricCoverage.SnapshotOnly, value.Coverage);

        // A save that could not read the value: the latest state is not filled from an older save, and an extreme
        // over the saves is a lower or upper bound only.
        var unread = profile.Import(Reading(colony, metric, "s4", 400, amount: null)).Played(colony, 400).View()
            .Row(colony, metric.StableKey).Value;
        if (selection == ColonySelection.Representative)
        {
            Assert.Equal(6, unread.Number);   // a provider's last capture, shown as partial
            Assert.Equal(MetricCoverage.Partial, unread.Coverage);
        }
        else
        {
            Assert.Equal(expected, unread.Number);
            Assert.Equal(MetricCoverage.Partial, unread.Coverage);
        }
    }

    /// <summary>A reading of <paramref name="metric"/> at one save: its scalar is the sum of the breakdown, or <paramref name="amount"/>.</summary>
    private static MetricObservation Reading(TestColony colony, ModMetricDescriptor metric, string key, int tick,
        params (string Key, long Amount)[] byDef)
        => Reading(colony, metric, key, tick, byDef.Length == 0 ? 3 : byDef.Sum(d => d.Amount), byDef);

    private static MetricObservation Reading(TestColony colony, ModMetricDescriptor metric, string key, int tick, long? amount,
        (string Key, long Amount)[]? byDef = null, MetricCoverage? coverage = null)
    {
        var observation = new MetricObservation
        {
            Key = key, PayloadHash = key, Source = metric.Source, Identity = colony.Identity.Clone(), SaveTick = tick,
            ParentKey = metric.Source == MetricObservationSource.Checkpoint && !key.EndsWith('1') ? key[..^1] + (int.Parse(key[^1..]) - 1) : null,
            ObservedUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc).AddSeconds(tick),
        };
        if (amount is null)
            return observation;
        var measured = coverage ?? (metric.Source == MetricObservationSource.Checkpoint ? MetricCoverage.Complete : MetricCoverage.SnapshotOnly);
        MetricValue Value(long n) => new() { IntegerValue = n, Coverage = measured, SinceTick = 0, ThroughTick = tick };
        var entry = MetricEntry.Of(metric.Revision, Value(amount.Value));
        foreach (var (def, n) in byDef ?? [])
            entry.Values[def] = Value(n);
        observation.Metrics[metric.StableKey] = entry;
        return observation;
    }
}
