namespace RimworldStatistics.Core.Tests;

/// <summary>Lifetime presentation and persisted family breakdowns: independent of game hooks.</summary>
public sealed class LifetimeCumulativeTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static MetricObservation Observe(LiveMetricAccumulator a, int tick = 200)
    {
        var d = a.PrepareSave(tick, Now, "test")!;
        a.Commit(d);
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "test" }, null, null);
    }

    private static (LiveMetricAccumulator Acc, FamilyState State) NewGame(LiveFamily family)
    {
        var a = LiveMetricAccumulator.StartNew(new[] { family.Spec }, 0);
        return (a, a.Family(family.Name)!);
    }

    private static MetricValue Scalar(MetricObservation o, string key)
        => o.Scalar(key, MetricKeys.Revision(key))!;

    private static IReadOnlyDictionary<string, MetricValue> Children(MetricObservation o, string key)
        => o.Metrics[key].Values.Where(x => x.Key != MetricEntry.Scalar)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    [Fact]
    public void Trade_ExistingTotalRemainsMoneyAndUnitsBreakDownByItem()
    {
        var (a, state) = NewGame(TradeFamily.Family);
        TradeFamily.Traded(state, TradeFamily.Orbital, null, new[]
        {
            new TradeLine { Def = "Steel", Count = 10, Total = 25, PlayerSells = true },
            new TradeLine { Def = "Component", Count = 2, Total = 60, PlayerSells = true },
            new TradeLine { Def = "Medicine", Count = 3, Total = 45, PlayerSells = false },
        });
        var o = Observe(a);
        Assert.Equal(1, Scalar(o, TradeFamily.Deals).IntegerValue);
        Assert.Equal(1, Children(o, TradeFamily.Deals)[TradeFamily.Orbital].IntegerValue);
        Assert.Equal(12, Scalar(o, TradeFamily.SalesUnits).IntegerValue);
        Assert.Equal(10, Children(o, TradeFamily.SalesUnits)["Steel"].IntegerValue);
        Assert.Equal(2, Children(o, TradeFamily.SalesUnits)["Component"].IntegerValue);
        Assert.Equal(3, Scalar(o, TradeFamily.BuysUnits).IntegerValue);
        Assert.Equal(3, Children(o, TradeFamily.BuysUnits)["Medicine"].IntegerValue);
        Assert.Equal(85, Scalar(o, TradeFamily.SalesTotal).RealValue);
    }

    [Fact]
    public void SowingAndSurgery_RevealOutcomesWithoutNewCounters()
    {
        var (w, ws) = NewGame(WorkFamily.Family);
        WorkFamily.Sown(ws, "plant1", "Rice");
        WorkFamily.Sown(ws, "plant2", "Rice");
        WorkFamily.Sown(ws, "plant3", "Corn");
        var wo = Observe(w);
        Assert.Equal(3, Scalar(wo, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(2, Children(wo, WorkFamily.Sown_)["Rice"].IntegerValue);
        Assert.Equal(1, Children(wo, WorkFamily.Sown_)["Corn"].IntegerValue);

        var (m, ms) = NewGame(MedicineFamily.Family);
        MedicineFamily.Surgery(ms, "surgeon", true, false);
        MedicineFamily.Surgery(ms, "surgeon", true, false);
        MedicineFamily.Surgery(ms, "surgeon", false, false);
        var mo = Observe(m);
        Assert.Equal(3, Scalar(mo, MedicineFamily.Surgeries).IntegerValue);
        Assert.Equal(2, Children(mo, MedicineFamily.Surgeries)["success"].IntegerValue);
        Assert.Equal(1, Children(mo, MedicineFamily.Surgeries)["failure"].IntegerValue);
        Assert.Equal(2.0 / 3.0, Scalar(mo, MedicineFamily.SurgerySuccessRate).RealValue!.Value, 10);
    }

    [Fact]
    public void Overflow_WeakensChildrenButNotMeasuredParent()
    {
        var (a, s) = NewGame(WorkFamily.Family);
        for (int i = 0; i <= FamilyOps.KeyCap; i++)
            s.CountBy("sown", "crop-" + i);
        s.Count("sown", FamilyOps.KeyCap + 1);
        var o = Observe(a);
        Assert.Equal(FamilyOps.KeyCap + 1, Scalar(o, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(MetricCoverage.Complete, Scalar(o, WorkFamily.Sown_).Coverage);
        Assert.Equal(FamilyOps.KeyCap, Children(o, WorkFamily.Sown_).Count);
        Assert.All(Children(o, WorkFamily.Sown_).Values, v => Assert.Equal(MetricCoverage.Partial, v.Coverage));
    }

    [Fact]
    public void LifetimeView_IsNotAnAggregationOrAUnitHeuristic()
    {
        Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == TradeFamily.Deals)));
        Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == WorkFamily.Sown_)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == TradeFamily.SalesPeak)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == MedicineFamily.SurgerySuccessRate)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == MetricKeys.WealthItemsLatest)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(new MetricDefinition
        {
            Key = TradeFamily.Deals, StoredKey = TradeFamily.Deals, LedgerIds = "external", Category = MetricCategory.Economy,
            Unit = MetricUnit.Count, Provenance = MetricProvenance.LiveAggregate,
            Overall = OverallAggregation.Sum, LabelKey = "external", DescriptionKey = "external", ProviderId = "example",
        }));
        Assert.Equal(MetricRegistry.Catalog.Count, MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog, MetricDisplayPolicy.AllDetailsVisibleUnits).Count);
    }

    [Fact]
    public void OldCheckpoint_IsReevaluatedWithoutMutatingFamilyRevisions()
    {
        Assert.Equal(10, SaveReader.Version);
        Assert.Equal(1, TradeFamily.Family.Revision);
        Assert.Equal(1, WorkFamily.Family.Revision);
        Assert.Equal(2, MedicineFamily.Family.Revision);
        var (a, s) = NewGame(WorkFamily.Family);
        WorkFamily.Sown(s, "plant", "Rice");
        var draft = a.PrepareSave(100, Now, "legacy")!;
        var parsed = CheckpointJson.Parse(draft.Json).Payload!;
        var o = CheckpointMetrics.ToObservation(parsed, draft.Json, new ColonyIdentity { ModCampaignId = "legacy" }, null, null);
        Assert.Equal(1, Scalar(o, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(1, Children(o, WorkFamily.Sown_)["Rice"].IntegerValue);
    }
}
