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
