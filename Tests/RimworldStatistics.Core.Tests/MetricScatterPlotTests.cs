using System.Diagnostics;

namespace RimworldStatistics.Core.Tests;

public sealed class MetricScatterPlotTests
{
    private static readonly MetricDefinition Quests = MetricRegistry.Find(MetricKeys.QuestSuccess)!;
    private static readonly MetricDefinition Mood = MetricRegistry.Find(MetricKeys.MoodMean)!;
    private static readonly MetricDefinition First100K = MetricRegistry.Find(MetricKeys.WealthTotalFirst100K)!;

    [Fact]
    public void EveryPointMatchesTheRankingsAndColonyChartValues()
    {
        var view = View(
            Make("a", ticks: 600_000, wealth: 120_000, rows: [Row(Quests, 9, ticks: 600_000), Row(Mood, 0.61, MetricCoverage.SnapshotOnly, 600_000)]),
            Make("b", ticks: 300_000, wealth: 80_000, rows: [Row(Quests, 0, ticks: 300_000), Row(Mood, 0.44, MetricCoverage.Partial, 300_000)]),
            Make("c", ticks: 900_000, wealth: 400_000, rows: [Row(Quests, 21, MetricCoverage.Partial, 900_000)]),
            Make("d", ticks: 60_000, wealth: 10_000, rows: [Row(Mood, 0.70, ticks: 60_000)]));

        foreach (var (xKey, yKey) in new[]
        {
            (BasicMetrics.Days, MetricKeys.QuestSuccess), (BasicMetrics.PeakWealth, MetricKeys.MoodMean),
            (MetricKeys.QuestSuccess, MetricKeys.MoodMean), (BasicMetrics.Raids, BasicMetrics.Days),
        })
        {
            var plot = MetricScatterPlot.Build(view, xKey, yKey)!;
            var xBoard = LeaderboardBuilder.Build(view, xKey, descending: true).Entries.ToDictionary(e => e.ColonyKey);
            var yBoard = LeaderboardBuilder.Build(view, yKey, descending: true).Entries.ToDictionary(e => e.ColonyKey);
            var xBars = Bars(view, xKey);
            var yBars = Bars(view, yKey);

            // Exactly the colonies ranked on both statistics have a point, with the very values ranked and charted.
            Assert.Equal(xBoard.Keys.Intersect(yBoard.Keys).Order(StringComparer.Ordinal), plot.Points.Select(p => p.ColonyKey));
            foreach (var point in plot.Points)
            {
                Assert.Equal(xBoard[point.ColonyKey].Value, point.X.Number);
                Assert.Equal(yBoard[point.ColonyKey].Value, point.Y.Number);
                Assert.Equal((xBoard[point.ColonyKey].Coverage, yBoard[point.ColonyKey].Coverage), (point.X.Coverage, point.Y.Coverage));
                Assert.Equal((xBoard[point.ColonyKey].ThroughTick, yBoard[point.ColonyKey].ThroughTick), (point.X.ThroughTick, point.Y.ThroughTick));
                Assert.Equal(xBars[point.ColonyKey], point.X.Number);
                Assert.Equal(yBars[point.ColonyKey], point.Y.Number);
            }
            Assert.Equal(view.Stats.Colonies.Count, plot.ColonyCount);
        }
    }

    [Fact]
    public void ColoniesWithoutBothValuesAreListedByReasonAndNeverDrawnAtZero()
    {
        var view = View(
            Make("ok", rows: [Row(Quests, 0), Row(Mood, 0)]),
            Make("no-x", rows: [Row(Mood, 0.5)]),
            Make("no-y", rows: [Row(Quests, 4)]),
            Make("none", rows: []),
            Make("unknown", rows: [Row(Quests, null, MetricCoverage.Unknown), Row(Mood, 0.5)]),
            Make("na", rows: [Row(Quests, null, MetricCoverage.NotApplicable), Row(Mood, null, MetricCoverage.Unknown)]),
            Make("zero-over-zero", rows: [Row(Quests, 3), new NumericRow
            {
                Definition = Mood,
                Value = new NumericValue { Coverage = MetricCoverage.Complete, Numerator = 0, Denominator = 0 },
            }]),
            Make("by-category", rows: [Row(Quests, 3), new NumericRow
            {
                Definition = Mood,
                Value = new NumericValue { Coverage = MetricCoverage.Complete, BreakdownOnly = true },
            }]),
            Make("nan", rows: [Row(Quests, double.NaN), Row(Mood, 0.5)]),
            Make("excluded", excluded: true, rows: [Row(Quests, 1), Row(Mood, 0.5)]),
            Make("elsewhere", outOfScope: true, rows: [Row(Quests, 1), Row(Mood, 0.5)]));

        var plot = MetricScatterPlot.Build(view, MetricKeys.QuestSuccess, MetricKeys.MoodMean)!;

        // A measured 0 is a point; nothing else becomes one.
        var point = Assert.Single(plot.Points);
        Assert.Equal(("ok", 0d, 0d), (point.ColonyKey, point.X.Number, point.Y.Number));
        Assert.Equal(view.Stats.Colonies.Count - 1, plot.Exclusions.Count);
        Assert.Equal(new[]
        {
            new ScatterExclusion("excluded", ScatterExclusionReason.Excluded, ScatterAxes.None),
            new ScatterExclusion("elsewhere", ScatterExclusionReason.OutOfScope, ScatterAxes.None),
            // Not applicable on X outranks the unknown Y.
            new ScatterExclusion("na", ScatterExclusionReason.NotApplicable, ScatterAxes.X),
            new ScatterExclusion("by-category", ScatterExclusionReason.NoSingleValue, ScatterAxes.Y),
            new ScatterExclusion("zero-over-zero", ScatterExclusionReason.NoSingleValue, ScatterAxes.Y),
            new ScatterExclusion("nan", ScatterExclusionReason.Missing, ScatterAxes.X),
            new ScatterExclusion("no-x", ScatterExclusionReason.Missing, ScatterAxes.X),
            new ScatterExclusion("no-y", ScatterExclusionReason.Missing, ScatterAxes.Y),
            new ScatterExclusion("none", ScatterExclusionReason.Missing, ScatterAxes.Both),
            new ScatterExclusion("unknown", ScatterExclusionReason.Missing, ScatterAxes.X),
        }, plot.Exclusions);
        Assert.Equal(3, plot.Count(ScatterExclusionReason.Missing, ScatterAxes.X));
        Assert.Equal(1, plot.Count(ScatterExclusionReason.Missing, ScatterAxes.Y));
        Assert.Equal(1, plot.Count(ScatterExclusionReason.Missing, ScatterAxes.Both));
        Assert.Equal(5, plot.Count(ScatterExclusionReason.Missing));
        Assert.True(plot.TooFewPoints);
    }

    [Fact]
    public void AThresholdNotReachedIsItsOwnReasonNotMissingData()
    {
        var view = View(
            Make("reached", rows: [Row(First100K, 41.5)]),
            Make("never", rows: [Row(First100K, null)]),
            Make("unrecorded", rows: [Row(First100K, null, MetricCoverage.Unknown)]));

        var plot = MetricScatterPlot.Build(view, BasicMetrics.Days, MetricKeys.WealthTotalFirst100K)!;

        Assert.Equal("reached", Assert.Single(plot.Points).ColonyKey);
        Assert.Equal(new[]
        {
            new ScatterExclusion("never", ScatterExclusionReason.NotReached, ScatterAxes.Y),
            new ScatterExclusion("unrecorded", ScatterExclusionReason.Missing, ScatterAxes.Y),
        }, plot.Exclusions);
    }

    [Fact]
    public void ABasicStatisticWithoutAnObservedSaveIsMissingNotZero()
    {
        var endingOnly = Make("ending-only", rows: [Row(Quests, 2)]);
        var view = View(Make("seen", rows: [Row(Quests, 2)]), endingOnly with { Stats = Stats("ending-only", observed: false) });

        var plot = MetricScatterPlot.Build(view, BasicMetrics.Raids, MetricKeys.QuestSuccess)!;

        Assert.Equal("seen", Assert.Single(plot.Points).ColonyKey);
        Assert.Equal(new ScatterExclusion("ending-only", ScatterExclusionReason.Missing, ScatterAxes.X), Assert.Single(plot.Exclusions));
    }

    [Fact]
    public void PartialAndDifferentlyTimedPointsAreDrawnButMarked()
    {
        var view = View(
            Make("same", ticks: 600_000, rows: [Row(Quests, 3, ticks: 600_000)]),
            Make("partial", ticks: 600_000, rows: [Row(Quests, 3, MetricCoverage.Partial, 600_000)]),
            // The numeric reading runs to an older save than the colony's latest one.
            Make("older", ticks: 600_000, rows: [Row(Quests, 3, ticks: 480_000)]),
            Make("untimed", ticks: 600_000, rows: [Row(Quests, 3, ticks: null)]));

        var plot = MetricScatterPlot.Build(view, BasicMetrics.Days, MetricKeys.QuestSuccess)!;
        var byKey = plot.Points.ToDictionary(p => p.ColonyKey);

        Assert.False(byKey["same"].NeedsCaution);
        Assert.True(byKey["partial"].HasPartial);
        Assert.False(byKey["partial"].ObservedAtDifferentTicks);
        Assert.True(byKey["older"].ObservedAtDifferentTicks);
        Assert.False(byKey["untimed"].NeedsCaution);
        Assert.Equal(2, plot.CautionCount);
    }

    [Fact]
    public void RankingsOnlyAndDerivedStatisticsCannotBePlotted()
    {
        var ranking = new MetricDefinition
        {
            Key = "test.scatter.ranking", StoredKey = "test.scatter.ranking", LedgerIds = "test",
            Category = MetricCategory.Colony, Unit = MetricUnit.Count, Provenance = MetricProvenance.SaveHistory,
            Overall = OverallAggregation.Ranking, LabelKey = "x", DescriptionKey = "x",
        };
        var derived = ranking with { Key = CrossColonyCatalog.MaxPrefix + MetricKeys.QuestSuccess };
        var view = View(Make("a", rows: [Row(Quests, 1), Row(ranking, 1), Row(derived, 1)]));

        Assert.True(MetricScatterPlot.CanPlot(view, BasicMetrics.Days));
        Assert.True(MetricScatterPlot.CanPlot(view, MetricKeys.QuestSuccess));
        Assert.False(MetricScatterPlot.CanPlot(view, ranking.Key));
        Assert.False(MetricScatterPlot.CanPlot(view, derived.Key));
        Assert.False(MetricScatterPlot.CanPlot(view, "no.such.metric"));
        Assert.Null(MetricScatterPlot.Build(view, ranking.Key, MetricKeys.QuestSuccess));
        Assert.Null(MetricScatterPlot.Build(view, BasicMetrics.Days, "no.such.metric"));
        // Every statistic the rankings offer can be plotted.
        Assert.All(LeaderboardBuilder.RankableKeys(view), key => Assert.True(MetricScatterPlot.CanPlot(view, key)));
    }

    [Theory]
    [InlineData(new double[] { 5, 5, 5 })]
    [InlineData(new double[] { 0, 0 })]
    [InlineData(new double[] { -3_000, -12.5, 0 })]
    [InlineData(new double[] { -2e15, 1e15 })]
    [InlineData(new double[] { 1e15 })]
    [InlineData(new double[] { 0.25, 0.26, 0.27 })]
    public void AxesStayFiniteZeroInclusiveAndCoverEveryValue(double[] values)
    {
        var view = View(values.Select((v, i) => Make($"c{i:D2}", rows: [Row(Quests, v), Row(Mood, v)])).ToArray());

        var plot = MetricScatterPlot.Build(view, MetricKeys.QuestSuccess, MetricKeys.MoodMean)!;

        Assert.Equal(values.Length, plot.Points.Count);
        foreach (var axis in new[] { plot.X, plot.Y })
        {
            Assert.True(axis.Ticks.Count >= 2);
            Assert.All(axis.Ticks, t => Assert.True(double.IsFinite(t)));
            Assert.Contains(0d, axis.Ticks);
            Assert.True(axis.Ticks[^1] > axis.Ticks[0]);
            Assert.True(axis.Ticks[0] <= values.Min() && axis.Ticks[^1] >= values.Max());
            Assert.All(values, v => Assert.InRange(axis.Position(v), 0d, 1d));
        }
        Assert.Equal(0d, plot.X.Position(plot.X.Ticks[0]));
        Assert.Equal(1d, plot.X.Position(plot.X.Ticks[^1]));
    }

    [Fact]
    public void AnEmptyPlotStillHasAUsableAxis()
    {
        var view = View(Make("a", rows: [Row(Quests, null, MetricCoverage.Unknown), Row(Mood, null, MetricCoverage.NotApplicable)]));
        var plot = MetricScatterPlot.Build(view, MetricKeys.QuestSuccess, MetricKeys.MoodMean)!;

        Assert.Empty(plot.Points);
        Assert.Equal(new[] { 0d, 1d }, plot.X.Ticks);
        Assert.Equal(new[] { 0d, 1d }, plot.Y.Ticks);
        Assert.Empty(plot.Cluster(400, 300, 8));
        Assert.Equal(0.5d, new ScatterAxis("k", Quests, false, ColonyReportValueKind.CumulativeTotal, [3d, 3d]).Position(3d));
    }

    [Fact]
    public void MarkersThatWouldCoverOneAnotherAreDrawnOnceWithTheirCount()
    {
        var view = View(
            Make("a", rows: [Row(Quests, 10), Row(Mood, 10)]),
            Make("b", rows: [Row(Quests, 10), Row(Mood, 10)]),
            Make("c", rows: [Row(Quests, 10.01), Row(Mood, 10)]),
            Make("d", rows: [Row(Quests, 0), Row(Mood, 0)]),
            Make("e", rows: [Row(Quests, 5), Row(Mood, 10)]));

        var plot = MetricScatterPlot.Build(view, MetricKeys.QuestSuccess, MetricKeys.MoodMean)!;
        var clusters = plot.Cluster(400, 300, 8);

        Assert.Equal(3, clusters.Count);
        Assert.Equal(new[] { 0, 1, 2 }, clusters[0].PointIndices);
        Assert.Equal(new[] { 3 }, clusters[1].PointIndices);
        Assert.Equal(new[] { 4 }, clusters[2].PointIndices);
        Assert.Equal(plot.Points.Count, clusters.Sum(c => c.PointIndices.Count));
        Assert.All(clusters, c => Assert.True(c.X is >= 0 and <= 400 && c.Y is >= 0 and <= 300));
        // A larger area separates what a smaller one stacks.
        Assert.Equal(4, plot.Cluster(400_000, 300, 8).Count);
    }

    [Fact]
    public void GroupScopeAndExclusionFollowTheView()
    {
        var all = View(Make("a", rows: [Row(Quests, 1)]), Make("b", rows: [Row(Quests, 2)]), Make("c", rows: [Row(Quests, 3)]));
        var group = new ColonyGroup { Id = "g", Name = "G", ColonyKeys = ["a", "c"] };
        var scoped = Rescope(all, ViewScope.ForGroup(group), excluded: "c");

        var plot = MetricScatterPlot.Build(scoped, BasicMetrics.Days, MetricKeys.QuestSuccess)!;

        Assert.Equal("a", Assert.Single(plot.Points).ColonyKey);
        Assert.Equal(new[]
        {
            new ScatterExclusion("c", ScatterExclusionReason.Excluded, ScatterAxes.None),
            new ScatterExclusion("b", ScatterExclusionReason.OutOfScope, ScatterAxes.None),
        }, plot.Exclusions);
    }

    [Fact]
    public void BuildingIsDeterministicAndReadOnly()
    {
        var view = View(Make("b", rows: [Row(Quests, 2), Row(Mood, 0.3)]), Make("a", rows: [Row(Quests, 1), Row(Mood, 0.4)]));
        var before = Snapshot(view);

        var first = MetricScatterPlot.Build(view, MetricKeys.QuestSuccess, MetricKeys.MoodMean)!;
        var second = MetricScatterPlot.Build(view, MetricKeys.QuestSuccess, MetricKeys.MoodMean)!;

        Assert.Equal(first.Points, second.Points);
        Assert.Equal(new[] { "a", "b" }, first.Points.Select(p => p.ColonyKey));
        Assert.Equal(before, Snapshot(view));
    }

    [Fact]
    public void FiveHundredColoniesBuildAndClusterQuickly()
    {
        var filler = Enumerable.Range(0, 300).Select(i => Quests with { Key = $"test.scatter.filler.{i:D3}", StoredKey = $"test.scatter.filler.{i:D3}" }).ToArray();
        var colonies = Enumerable.Range(0, 500).Select(i =>
        {
            var rows = filler.Select(d => Row(d, i)).ToList();
            rows.Add(Row(Quests, i % 37));
            if (i % 10 != 0)
                rows.Add(Row(Mood, (i % 100) / 100d, i % 7 == 0 ? MetricCoverage.Partial : MetricCoverage.Complete));
            return Make($"colony-{i:D4}", ticks: 60_000 * (1 + i % 50), wealth: 1_000d * i, rows: [.. rows]);
        }).ToArray();
        var view = View(colonies);

        MetricScatterPlot.Build(view, BasicMetrics.Days, MetricKeys.MoodMean);   // JIT warm-up
        var watch = Stopwatch.StartNew();
        var plot = MetricScatterPlot.Build(view, MetricKeys.QuestSuccess, MetricKeys.MoodMean)!;
        var buildMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var clusters = plot.Cluster(900, 520, 7);
        var clusterMs = watch.Elapsed.TotalMilliseconds;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"scatter: 500 colonies × 302 rows, build {buildMs:F1} ms, cluster {clusterMs:F1} ms, {clusters.Count} markers");

        Assert.Equal(450, plot.Points.Count);
        Assert.Equal(50, plot.Count(ScatterExclusionReason.Missing, ScatterAxes.Y));
        Assert.Equal(plot.Points.Count, clusters.Sum(c => c.PointIndices.Count));
        Assert.InRange(buildMs, 0, 2000);
        Assert.InRange(clusterMs, 0, 500);
    }

    // ----- fixtures -----

    private sealed record ColonyFixture(ColonyStats Stats, NumericColony Numeric);

    private static ColonyStats Stats(string key, int ticks = 600_000, double wealth = 50_000, bool excluded = false,
        bool outOfScope = false, bool observed = true) => new()
    {
        Key = key, Excluded = excluded, OutOfScope = outOfScope,
        HasObservedPoint = observed, MaxTicks = ticks, MaxRealPlay = ticks / 60d,
        WealthMax = wealth, WealthLast = wealth * 0.8, PeakWealthKnown = true, LastWealthKnown = true,
        Raids = ticks / 60_000, Threats = ticks / 30_000, MaxPopulation = 5,
        SaveCount = 2, GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(),
    };

    private static ColonyFixture Make(string key, int ticks = 600_000, double wealth = 50_000, bool excluded = false,
        bool outOfScope = false, NumericRow[]? rows = null)
        => new(Stats(key, ticks, wealth, excluded, outOfScope),
            new NumericColony { Key = key, Excluded = excluded, OutOfScope = outOfScope, Rows = rows ?? [] });

    private static NumericRow Row(MetricDefinition definition, double? number,
        MetricCoverage coverage = MetricCoverage.Complete, int? ticks = 600_000) => new()
    {
        Definition = definition,
        Value = new NumericValue { Number = number, Coverage = coverage, ThroughTick = ticks, Evidence = MetricEvidence.Verified },
        Targets = coverage == MetricCoverage.NotApplicable ? 0 : 1,
        Valid = number is null ? 0 : 1,
    };

    private static StatsView View(params ColonyFixture[] colonies) => new()
    {
        Colonies = [],
        Stats = new StatsResult
        {
            Colonies = colonies.Select(c => c.Stats).ToArray(),
            ColonyCount = colonies.Count(c => !c.Stats.Excluded && !c.Stats.OutOfScope),
            EndingsByType = new Dictionary<EndingType, int>(),
        },
        Numeric = new NumericView { Colonies = colonies.Select(c => c.Numeric).ToArray(), Overall = [] },
    };

    /// <summary>The same colonies as a group view with one colony excluded, as the statistics window builds it.</summary>
    private static StatsView Rescope(StatsView view, ViewScope scope, string excluded)
    {
        var colonies = view.Numeric.Colonies.Select(n =>
        {
            var inScope = scope.Contains(new Colony { Key = n.Key });
            var stats = view.Stats.Colonies.Single(s => s.Key == n.Key);
            var isExcluded = n.Key == excluded;
            return new ColonyFixture(
                Stats(n.Key, stats.MaxTicks, stats.WealthMax, isExcluded, !inScope),
                new NumericColony { Key = n.Key, Excluded = isExcluded, OutOfScope = !inScope, Rows = n.Rows });
        }).ToArray();
        return View(colonies);
    }

    private static Dictionary<string, double> Bars(StatsView view, string key)
        => (BasicMetrics.Contains(key) ? MetricChartSeries.BasicByColony(view.Stats, key) : MetricChartSeries.ByColony(view.Numeric, key))
            .ToDictionary(b => b.Key, b => b.Value.Number!.Value);

    private static string Snapshot(StatsView view)
        => string.Join(";", view.Numeric.Colonies.SelectMany(c => c.Rows.Select(r =>
            $"{c.Key}:{r.Definition.Key}={r.Value.Number}/{r.Value.Coverage}/{r.Value.ThroughTick}")))
            + "|" + string.Join(";", view.Stats.Colonies.Select(s => $"{s.Key}:{s.MaxTicks}/{s.WealthMax}/{s.Excluded}/{s.OutOfScope}"));
}
