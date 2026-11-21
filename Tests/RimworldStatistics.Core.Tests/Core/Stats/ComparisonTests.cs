namespace RimworldStatistics.Core.Tests;

public sealed class ComparisonTests
{
    [Fact]
    public void UsesEachColumnsExistingRowAndKeepsMissingNull()
    {
        var metric = MetricRegistry.Find(MetricKeys.QuestSuccess)!;
        var colony = new NumericValue { Number = 7, Coverage = MetricCoverage.Partial, Evidence = MetricEvidence.Experimental };
        var scope = new NumericValue { Number = 3, Coverage = MetricCoverage.Complete };
        var rows = Comparison.Build(
            [(new ColonyTarget("a"), View(metric, colony, NumericValue.Unknown)),
                (new ScopeTarget(ViewScope.All), View(metric, NumericValue.Unknown, scope)),
                (new ColonyTarget("absent"), View(metric, NumericValue.Unknown, scope))],
            [metric.Key]);
        var result = Assert.Single(rows);
        Assert.Same(colony, result.Values[0]);
        Assert.Same(scope, result.Values[1]);
        Assert.Null(result.Values[2]);
    }

    [Fact]
    public void MoreThanFourColumnsAreRejected()
    {
        var view = View(MetricRegistry.Find(MetricKeys.QuestSuccess)!, NumericValue.Unknown, NumericValue.Unknown);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Comparison.Build(Enumerable.Repeat<(ComparisonTarget, StatsView)>((new ScopeTarget(ViewScope.All), view), 5).ToArray(), []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingNumbersSortLastInBothDirections(bool descending)
    {
        var rows = new ComparisonRow[]
        {
            new("unknown", [null]), new("higher", [new NumericValue { Number = 9, Coverage = MetricCoverage.Complete }]),
            new("lower", [new NumericValue { Number = 1, Coverage = MetricCoverage.Partial }]),
        };
        Assert.Equal(descending ? new[] { "higher", "lower", "unknown" } : new[] { "lower", "higher", "unknown" },
            Comparison.Sort(rows, 0, descending).Select(row => row.MetricKey));
    }

    private static StatsView View(MetricDefinition definition, NumericValue colony, NumericValue overall) => new()
    {
        Colonies = [],
        Stats = new StatsResult { Colonies = [], EndingsByType = new Dictionary<EndingType, int>() },
        Numeric = new NumericView
        {
            Colonies = [new NumericColony { Key = "a", Rows = [new NumericRow { Definition = definition, Value = colony }] }],
            Overall = [new NumericRow { Definition = definition, Value = overall }],
        },
    };
}
