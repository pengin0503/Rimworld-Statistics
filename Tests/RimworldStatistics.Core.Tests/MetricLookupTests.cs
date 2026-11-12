namespace RimworldStatistics.Core.Tests;

public sealed class MetricLookupTests
{
    private static StatsView Build()
    {
        var definition = MetricRegistry.All.First(d => d.Overall != OverallAggregation.IndividualOnly
            && d.Overall != OverallAggregation.Ranking);
        var value = new NumericValue { Number = 25, Coverage = MetricCoverage.Complete };
        var row = new NumericRow { Definition = definition, Value = value, Valid = 1, Targets = 1 };
        var stats = new ColonyStats
        {
            Key = "c1", GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(), 
            SaveCount = 1, MaxTicks = 120000, Raids = 3, PeakWealthKnown = true, WealthMax = 1000,
            LastWealthKnown = true, WealthLast = 500, HasObservedPoint = true,
        };
        return new StatsView
        {
            Colonies = [],
            Stats = new StatsResult { ColonyCount = 1, Colonies = [stats], EndingsByType = new Dictionary<EndingType, int>() },
            Numeric = new NumericView
            {
                Overall = [row],
                Colonies = [new NumericColony { Key = "c1", Rows = [row] }],
            },
        };
    }

    [Fact]
    public void FindsOverallAndPerColonyByStableKey()
    {
        var v = Build();
        var key = v.Numeric.Overall[0].Definition.Key;
        Assert.Same(v.Numeric.Overall[0], MetricLookup.Overall(v, key));
        Assert.Same(v.Numeric.Colonies[0].Rows[0], MetricLookup.ForColony(v, "c1", key));
        Assert.True(MetricLookup.IsKnownKey(v, key));
    }

    [Fact]
    public void MissingAndDeletedKeysNeverThrow()
    {
        var v = Build();
        Assert.Null(MetricLookup.Overall(v, "former.deleted.statistic"));
        Assert.Null(MetricLookup.Definition(v, "former.deleted.statistic"));
        Assert.Null(MetricLookup.ForColony(v, "c1", "former.deleted.statistic"));
        Assert.False(MetricLookup.IsKnownKey(v, "former.deleted.statistic"));
    }

    [Fact]
    public void BasicKeysCannotOverlapCatalogKeys()
    {
        Assert.All(BasicMetrics.Keys, key => Assert.StartsWith("basic.", key, StringComparison.Ordinal));
        Assert.DoesNotContain(MetricRegistry.Catalog, d => BasicMetrics.Keys.Contains(d.Key));
    }

    [Fact]
    public void BasicValueNeedsAnObservedPointAndWealthEvidence()
    {
        var c = new ColonyStats
        {
            Key = "c1", GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(),
            MaxTicks = 60000, Raids = 3, WealthMax = 100, WealthLast = 50,
            PeakWealthKnown = false, LastWealthKnown = false,
        };
        Assert.Null(BasicMetrics.Value(c, BasicMetrics.Days));
        Assert.Null(BasicMetrics.Value(c, BasicMetrics.PeakWealth));
        var observed = new ColonyStats
        {
            Key = "c1", GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(),
            HasObservedPoint = true, MaxTicks = 60000, Raids = 3, WealthMax = 100,
            PeakWealthKnown = false,
        };
        Assert.Equal(1d, BasicMetrics.Value(observed, BasicMetrics.Days));
        Assert.Equal(3d, BasicMetrics.Value(observed, BasicMetrics.Raids));
        Assert.Null(BasicMetrics.Value(observed, BasicMetrics.PeakWealth));
    }

    [Fact]
    public void EmptyViewAndIndividualOnlyHaveNoOverallRow()
    {
        var empty = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult { Colonies = [], EndingsByType = new Dictionary<EndingType, int>() },
        };
        Assert.Null(MetricLookup.Overall(empty, "unavailable"));
        Assert.Null(BasicMetrics.OverallValue(empty.Stats, BasicMetrics.Days));
    }
}
