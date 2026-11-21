namespace RimworldStatistics.Core.Tests;

public sealed class DerivedMetricGroupTests
{
    private static NumericRow DummyRow(MetricDefinition definition, double? value = 10d, MetricCoverage? coverage = null)
        => new()
        {
            Definition = definition,
            Value = new NumericValue
            {
                Number = value,
                Coverage = coverage ?? (value is null ? MetricCoverage.Unknown : MetricCoverage.Complete),
            },
            Targets = 1,
            Valid = value is null ? 0 : 1,
        };

    [Fact]
    public void RepresentativeCatalogDerivedMetricsAttachStrictlyByStoredKey()
    {
        var keys = new[]
        {
            TradeFamily.BuysTotal,
            CrossColonyCatalog.MaxPrefix + TradeFamily.BuysTotal,
            CrossColonyCatalog.MinPrefix + TradeFamily.BuysTotal,
            TradeFamily.SalesTotal,
            CrossColonyCatalog.MaxPrefix + TradeFamily.SalesTotal,
            CrossColonyCatalog.MinPrefix + TradeFamily.SalesTotal,
            TradeFamily.Deals,
            CrossColonyCatalog.MaxPrefix + TradeFamily.Deals,
            CrossColonyCatalog.MinPrefix + TradeFamily.Deals,
            CrossColonyCatalog.TopPrefix + TradeFamily.Deals,
            WorkFamily.HarvestUnits,
            CrossColonyCatalog.MaxPrefix + WorkFamily.HarvestUnits,
            CrossColonyCatalog.MinPrefix + WorkFamily.HarvestUnits,
            WorkFamily.CraftedUnits,
            CrossColonyCatalog.MaxPrefix + WorkFamily.CraftedUnits,
            CrossColonyCatalog.MinPrefix + WorkFamily.CraftedUnits,
            StockSnapshot.FoodLatest,
            CrossColonyCatalog.MaxPrefix + StockSnapshot.FoodLatest,
            CrossColonyCatalog.MinPrefix + StockSnapshot.FoodLatest,
            StockSnapshot.MaterialsLatest,
            CrossColonyCatalog.MaxPrefix + StockSnapshot.MaterialsLatest,
            CrossColonyCatalog.MinPrefix + StockSnapshot.MaterialsLatest,
            CrossColonyCatalog.TopPrefix + StockSnapshot.MaterialsLatest,
            RosterSnapshot.Passions,
            CrossColonyCatalog.SumPrefix + RosterSnapshot.Passions,
            CrossColonyCatalog.TopPrefix + RosterSnapshot.Passions,
        };

        var rows = keys
            .Select(k => MetricRegistry.Find(k) ?? throw new KeyNotFoundException(k))
            .Select(d => DummyRow(d, 42d))
            .ToArray();

        var grouped = DerivedMetricGroups.Group(rows, r => r.Definition, enableGrouping: true);

        Assert.Equal(8, grouped.Count);
        Assert.Equal(rows.Length, grouped.Sum(n => 1 + n.Children.Count));

        var dealsGroup = Assert.Single(grouped, n => n.Item.Definition.Key == TradeFamily.Deals);
        Assert.Equal(
            [
                CrossColonyCatalog.MaxPrefix + TradeFamily.Deals,
                CrossColonyCatalog.MinPrefix + TradeFamily.Deals,
                CrossColonyCatalog.TopPrefix + TradeFamily.Deals,
            ],
            dealsGroup.Children.Select(c => c.Definition.Key).ToArray());

        var passionsGroup = Assert.Single(grouped, n => n.Item.Definition.Key == RosterSnapshot.Passions);
        Assert.Equal(
            [
                CrossColonyCatalog.SumPrefix + RosterSnapshot.Passions,
                CrossColonyCatalog.TopPrefix + RosterSnapshot.Passions,
            ],
            passionsGroup.Children.Select(c => c.Definition.Key).ToArray());
    }

    [Fact]
    public void UnrelatedMetricsWithSamePrefixOrSameValueAreNeverGroupedOrDropped()
    {
        var itemsPeak = MetricRegistry.Find(MetricKeys.WealthItemsPeak)!;
        var itemsLatest = MetricRegistry.Find(MetricKeys.WealthItemsLatest)!;
        var itemsMax = MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + MetricKeys.WealthItemsLatest)!;
        var itemsMin = MetricRegistry.Find(CrossColonyCatalog.MinPrefix + MetricKeys.WealthItemsLatest)!;
        var modsLatest = MetricRegistry.Find(MetricKeys.ModsLatest)!;
        var modsPeak = MetricRegistry.Find("colony.mods.peak")!;
        var modsMin = MetricRegistry.Find("colony.mods.min")!;

        var rows = new[]
        {
            DummyRow(itemsPeak, 100d),
            DummyRow(itemsLatest, 100d),
            DummyRow(itemsMax, 100d),
            DummyRow(itemsMin, 100d),
            DummyRow(modsLatest, 100d),
            DummyRow(modsPeak, 100d),
            DummyRow(modsMin, 100d),
        };

        var grouped = DerivedMetricGroups.Group(rows, r => r.Definition, enableGrouping: true);

        Assert.Equal(5, grouped.Count);
        Assert.Empty(Assert.Single(grouped, n => n.Item.Definition.Key == MetricKeys.WealthItemsPeak).Children);
        Assert.Equal(2, Assert.Single(grouped, n => n.Item.Definition.Key == MetricKeys.WealthItemsLatest).Children.Count);
        Assert.Empty(Assert.Single(grouped, n => n.Item.Definition.Key == MetricKeys.ModsLatest).Children);
        Assert.Contains(grouped, n => n.Item.Definition.Key == "colony.mods.peak");
        Assert.Contains(grouped, n => n.Item.Definition.Key == "colony.mods.min");
        Assert.Equal(rows.Length, grouped.Sum(n => 1 + n.Children.Count));
    }

    [Fact]
    public void ExternalModMetricsWithCrossPrefixAreNeverGroupedAsDerived()
    {
        var baseMod = new MetricDefinition
        {
            Key = "mod.example.deals",
            StoredKey = "mod.example.deals",
            LedgerIds = "mod.example.deals",
            Category = MetricCategory.Economy,
            Unit = MetricUnit.Count,
            Provenance = MetricProvenance.SaveHistory,
            Overall = OverallAggregation.Sum,
            LabelKey = "mod.example.deals",
            DescriptionKey = "mod.example.deals",
            ProviderId = "example.provider",
            OwnerPackageId = "example.mod",
        };
        var similarMod = baseMod with
        {
            Key = CrossColonyCatalog.MaxPrefix + "mod.example.deals",
            StoredKey = "mod.example.deals",
            Overall = OverallAggregation.Ranking,
        };

        Assert.False(CrossColonyCatalog.IsDerived(similarMod));
        Assert.Null(DerivedMetricGroups.ParentKey(similarMod));

        var grouped = DerivedMetricGroups.Group([DummyRow(baseMod), DummyRow(similarMod)], r => r.Definition, enableGrouping: true);
        Assert.Equal(2, grouped.Count);
        Assert.All(grouped, n => Assert.Empty(n.Children));
    }

    [Fact]
    public void OrphanedDerivedMetricRemainsTopLevelWhenBaseMetricIsFilteredOut()
    {
        var derived = MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + TradeFamily.Deals)!;
        var partialDerived = DummyRow(derived, 12d, MetricCoverage.Partial);

        var grouped = DerivedMetricGroups.Group([partialDerived], r => r.Definition, enableGrouping: true);
        var node = Assert.Single(grouped);
        Assert.Equal(derived.Key, node.Item.Definition.Key);
        Assert.Empty(node.Children);
    }

    [Theory]
    [InlineData(true, false, false, "", true)]
    [InlineData(true, false, false, "   ", true)]
    [InlineData(false, false, false, "", false)]
    [InlineData(true, true, false, "", false)]
    [InlineData(true, false, true, "", false)]
    [InlineData(true, false, false, "trade", false)]
    public void ShouldGroupAppliesOnlyToAllColoniesNameSortWithoutSearch(
        bool groupDerived, bool perColony, bool byValue, string search, bool expected)
    {
        Assert.Equal(expected, DerivedMetricGroups.ShouldGroup(groupDerived, perColony, byValue, search));
    }

    [Fact]
    public void FlatModePreservesExactOrderAndZeroChildren()
    {
        var parent = MetricRegistry.Find(TradeFamily.Deals)!;
        var max = MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + TradeFamily.Deals)!;
        var min = MetricRegistry.Find(CrossColonyCatalog.MinPrefix + TradeFamily.Deals)!;
        var rows = new[] { DummyRow(max), DummyRow(parent), DummyRow(min) };

        var flat = DerivedMetricGroups.Group(rows, r => r.Definition, enableGrouping: false);
        Assert.Equal(rows.Select(r => r.Definition.Key), flat.Select(n => n.Item.Definition.Key));
        Assert.All(flat, n => Assert.Empty(n.Children));
    }

    [Theory]
    [InlineData(0, true, false, 600f)]
    [InlineData(0, true, true, 600f)]
    [InlineData(1, true, false, 600f)]
    [InlineData(1, true, true, 600f)]
    [InlineData(1, true, true, 120f)]
    [InlineData(1, false, true, 600f)]
    [InlineData(2, false, false, 600f)]
    public void HitboxesStayStrictlyDisjointAcrossDepthsAndExpandStates(
        int depth, bool isMetricRow, bool hasExpand, float width)
    {
        var hit = DerivedMetricGroups.ComputeHitboxes(10f, width, depth, isMetricRow, hasExpand);

        Assert.False(hit.Star.Overlaps(hit.Toggle));
        Assert.False(hit.Star.Overlaps(hit.Chart));
        Assert.False(hit.Toggle.Overlaps(hit.Chart));
        if (isMetricRow)
        {
            Assert.False(hit.Star.IsEmpty);
            Assert.Equal(!hasExpand, hit.Toggle.IsEmpty);
        }
        else
        {
            Assert.True(hit.Star.IsEmpty);
            Assert.True(hit.Chart.IsEmpty);
        }
    }
}
