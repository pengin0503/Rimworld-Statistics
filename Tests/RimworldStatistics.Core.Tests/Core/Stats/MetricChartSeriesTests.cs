namespace RimworldStatistics.Core.Tests;

public sealed class MetricChartSeriesTests
{
    private static readonly MetricDefinition Definition = MetricRegistry.Find(MetricKeys.QuestSuccess)!;

    [Fact]
    public void ColonySeriesIncludesMeasuredZeroAndPartialButNotUnknownOrExcluded()
    {
        var view = View(
            Colony("zero", 0),
            Colony("partial", 8, MetricCoverage.Partial),
            Colony("missing", null, MetricCoverage.Unknown),
            Colony("excluded", 100, excluded: true),
            Colony("other-scope", 200, outOfScope: true));

        var series = MetricChartSeries.ByColony(view, Definition.Key);

        Assert.Equal(new[] { "partial", "zero" }, series.Select(x => x.Key));
        Assert.Equal(new double[] { 8, 0 }, series.Select(x => x.Value.Number!.Value));
        Assert.Equal(MetricCoverage.Partial, series[0].Value.Coverage);
    }

    [Fact]
    public void ColonySeriesSortsNegativeValuesWithoutDroppingThem()
    {
        var view = View(Colony("negative", -12), Colony("positive", 3), Colony("zero", 0));
        var series = MetricChartSeries.ByColony(view, Definition.Key);
        Assert.Equal(new[] { "positive", "zero", "negative" }, series.Select(x => x.Key));
    }

    [Fact]
    public void BreakdownUsesSelectedColonyInsteadOfAllColonies()
    {
        var global = Row(9, [("global", 9)]);
        var first = Colony("first", 3, breakdowns: [("x", 2), ("y", 1)]);
        var second = Colony("second", 6, breakdowns: [("x", 6)]);
        var view = View([first, second], global);

        Assert.Equal(new[] { "x", "y" }, MetricChartSeries.Breakdowns(view, Definition.Key, "first").Select(x => x.Key));
        Assert.Equal(new[] { "global" }, MetricChartSeries.Breakdowns(view, Definition.Key).Select(x => x.Key));
        Assert.Empty(MetricChartSeries.Breakdowns(view, Definition.Key, "missing"));
    }

    [Fact]
    public void BreakdownExcludesUnknownValuesButPreservesZero()
    {
        var row = Row(5, [("known", 5), ("zero", 0)]);
        var missing = new NumericRow { Definition = Definition, Breakdown = "unknown", Value = new NumericValue { Coverage = MetricCoverage.Unknown } };
        var view = View([], new NumericRow
        {
            Definition = Definition, Value = row.Value, Breakdowns = row.Breakdowns.Concat([missing]).ToArray(),
        });

        Assert.Equal(new[] { "known", "zero" }, MetricChartSeries.Breakdowns(view, Definition.Key).Select(x => x.Key));
    }

    [Fact]
    public void OverallIsOnlyAvailableWhenNumberIsActuallyMeasured()
    {
        Assert.Single(MetricChartSeries.Overall(View([], Row(7)), Definition.Key));
        Assert.Empty(MetricChartSeries.Overall(View([], Row(null, coverage: MetricCoverage.Unknown)), Definition.Key));
    }

    [Fact]
    public void BasicStatisticsUseExistingColonyValuesWithoutInventingMissingRecords()
    {
        var stats = BasicStats(
            BasicColony("counted", 12),
            BasicColony("zero", 0),
            BasicColony("unreadable", 99, observed: false),
            BasicColony("excluded", 88, excluded: true),
            BasicColony("outside", 77, outOfScope: true));

        var result = MetricChartSeries.BasicByColony(stats, BasicMetrics.Raids);

        Assert.Equal(new[] { "counted", "zero" }, result.Select(bar => bar.Key));
        Assert.Equal(new double[] { 12, 0 }, result.Select(bar => bar.Value.Number!.Value));
    }

    [Fact]
    public void BasicWealthChartDoesNotTurnUnknownWealthIntoZero()
    {
        var stats = BasicStats(
            new ColonyStats
            {
                Key = "known", HasObservedPoint = true, PeakWealthKnown = true, WealthMax = 10,
                EndingCounts = new Dictionary<EndingType, int>(), GameVersions = [],
            },
            new ColonyStats
            {
                Key = "unknown", HasObservedPoint = true, WealthMax = 0,
                EndingCounts = new Dictionary<EndingType, int>(), GameVersions = [],
            });
        Assert.Equal(new[] { "known" }, MetricChartSeries.BasicByColony(stats, BasicMetrics.PeakWealth).Select(bar => bar.Key));
        Assert.Empty(MetricChartSeries.BasicByColony(stats, "basic.not-a-metric"));
    }

    [Theory]
    [InlineData(CrossColonyCatalog.MaxPrefix)]
    [InlineData(CrossColonyCatalog.MinPrefix)]
    public void DerivedExtremeComparesTheSourceValuesItSummarizes(string prefix)
    {
        var source = MetricRegistry.Find(RosterSnapshot.Free)!;
        var derived = MetricRegistry.Find(prefix + RosterSnapshot.Free)!;
        NumericColony SourceColony(string key, double number, bool excluded = false) => new()
        {
            Key = key, Excluded = excluded,
            Rows = [new NumericRow { Definition = source, Value = Number(number) }],
        };
        var view = new NumericView
        {
            Colonies = [SourceColony("a", 4), SourceColony("b", 9), SourceColony("hidden", 50, excluded: true)],
            Overall = [new NumericRow { Definition = derived, Value = Number(9) }],
        };

        var series = MetricChartSeries.ByColony(view, derived);

        Assert.Equal(new[] { "b", "a" }, series.Select(bar => bar.Key));
        // The derived key itself is never stored per colony.
        Assert.Empty(MetricChartSeries.ByColony(view, derived.Key));
    }

    [Fact]
    public void DerivedTopRankingAndOrdinaryMetricsKeepTheirOwnColonyKey()
    {
        var top = MetricRegistry.Find(CrossColonyCatalog.TopPrefix + WorkFamily.HarvestByCrop)!;
        Assert.Equal(top.Key, MetricChartSeries.ColonyComparisonKey(top));
        Assert.Equal(Definition.Key, MetricChartSeries.ColonyComparisonKey(Definition));
        Assert.Equal(RosterSnapshot.Free,
            MetricChartSeries.ColonyComparisonKey(MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + RosterSnapshot.Free)!));
    }

    private static StatsResult BasicStats(params ColonyStats[] colonies) => new()
    {
        Colonies = colonies,
        EndingsByType = new Dictionary<EndingType, int>(),
    };

    private static ColonyStats BasicColony(string key, int raids, bool observed = true, bool excluded = false, bool outOfScope = false) => new()
    {
        Key = key, Raids = raids, HasObservedPoint = observed, Excluded = excluded, OutOfScope = outOfScope,
        EndingCounts = new Dictionary<EndingType, int>(), GameVersions = [],
    };

    private static NumericValue Number(double? n, MetricCoverage coverage = MetricCoverage.Complete)
        => new() { Number = n, Coverage = coverage };

    private static NumericRow Row(double? n, (string, double)[]? breakdowns = null, MetricCoverage coverage = MetricCoverage.Complete)
        => new()
        {
            Definition = Definition,
            Value = Number(n, coverage),
            Breakdowns = breakdowns?.Select(b => new NumericRow { Definition = Definition, Breakdown = b.Item1, Value = Number(b.Item2) }).ToArray() ?? [],
        };

    private static NumericColony Colony(string key, double? number, MetricCoverage coverage = MetricCoverage.Complete,
        bool excluded = false, bool outOfScope = false, (string, double)[]? breakdowns = null)
        => new() { Key = key, Excluded = excluded, OutOfScope = outOfScope, Rows = [Row(number, breakdowns, coverage)] };

    private static NumericView View(params NumericColony[] colonies) => View(colonies, Row(null, coverage: MetricCoverage.Unknown));
    private static NumericView View(NumericColony[] colonies, NumericRow overall)
        => new() { Colonies = colonies, Overall = [overall] };
}
