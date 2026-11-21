namespace RimworldStatistics.Core.Tests;

public sealed class WorkDetailsLifetimeTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    private static CraftedThing Product(string def, int count) => new() { Def = def, Count = count };
    private static MetricObservation Save(LiveMetricAccumulator a, int tick = 500)
    {
        var draft = a.PrepareSave(tick, T0, "D" + tick)!;
        a.Commit(draft);
        return CheckpointMetrics.ToObservation(draft.Payload!, draft.Json, new ColonyIdentity { ModCampaignId = "work" }, null, null);
    }
    private static long? Scalar(MetricObservation o, string key) => o.Scalar(key, MetricKeys.Revision(key))!.IntegerValue;
    private static long? Child(MetricObservation o, string key, string def) => o.Metrics[key].Values[def].IntegerValue;

    [Fact]
    public void WorkDetailsAlone_IsIndependentOfOriginalWork()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { WorkDetailsFamily.Family.Spec }, 0);
        var state = a.Family(WorkDetailsFamily.Name)!;
        WorkDetailsFamily.CraftedProducts(state, new[] { Product("Steel", 3), Product("Steel", 2), Product("Medicine", 4), Product("Absent", 0) });
        WorkDetailsFamily.MinedYield(state, "Steel", 6);
        WorkDetailsFamily.MinedYield(state, "Steel", 0);
        var o = Save(a);
        Assert.Equal(9, Scalar(o, WorkDetailsFamily.CraftedByDef));
        Assert.Equal(5, Child(o, WorkDetailsFamily.CraftedByDef, "Steel"));
        Assert.Equal(4, Child(o, WorkDetailsFamily.CraftedByDef, "Medicine"));
        Assert.False(o.Metrics[WorkDetailsFamily.CraftedByDef].Values.ContainsKey("Absent"));
        Assert.Equal(6, Child(o, WorkDetailsFamily.MinedByDef, "Steel"));
        Assert.False(o.Metrics.ContainsKey(WorkFamily.CraftedUnits));
    }

    [Fact]
    public void BothFamilies_RetainSeparateCountsThroughLoad()
    {
        var specs = new[] { WorkFamily.Family.Spec, WorkDetailsFamily.Family.Spec };
        var a = LiveMetricAccumulator.StartNew(specs, 0);
        var items = new[] { Product("Steel", 3) };
        WorkFamily.Crafted(a.Family(WorkFamily.Name)!, "worker", items, 1);
        WorkDetailsFamily.CraftedProducts(a.Family(WorkDetailsFamily.Name)!, items);
        var draft = a.PrepareSave(100, T0, "parent")!;
        a.Commit(draft);
        var loaded = LiveMetricAccumulator.Resume(draft.Json, specs, 100);
        var more = new[] { Product("Steel", 5), Product("Medicine", 2) };
        WorkFamily.Crafted(loaded.Family(WorkFamily.Name)!, "worker", more, 120);
        WorkDetailsFamily.CraftedProducts(loaded.Family(WorkDetailsFamily.Name)!, more);
        var o = Save(loaded);
        Assert.Equal(10, Scalar(o, WorkFamily.CraftedUnits));
        Assert.Equal(10, Scalar(o, WorkDetailsFamily.CraftedByDef));
        Assert.Equal(8, Child(o, WorkDetailsFamily.CraftedByDef, "Steel"));
        Assert.Equal(2, Child(o, WorkDetailsFamily.CraftedByDef, "Medicine"));
        Assert.Equal(MetricCoverage.Complete, o.Scalar(WorkFamily.CraftedUnits, 1)!.Coverage);
    }

    [Fact]
    public void EnablingNewFamilyOnOldSave_DoesNotRestartOldCounterOrBackfillMissingTime()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { WorkFamily.Family.Spec }, 0);
        WorkFamily.Crafted(a.Family(WorkFamily.Name)!, "worker", new[] { Product("Component", 4) }, 12);
        var draft = a.PrepareSave(100, T0, "legacy")!;
        var loaded = LiveMetricAccumulator.Resume(draft.Json, new[] { WorkFamily.Family.Spec, WorkDetailsFamily.Family.Spec }, 100);
        Assert.Equal(1, WorkFamily.Family.Revision);
        Assert.Equal(100, loaded.Family(WorkDetailsFamily.Name)!.SinceTick);
        WorkDetailsFamily.CraftedProducts(loaded.Family(WorkDetailsFamily.Name)!, new[] { Product("Component", 2) });
        var o = Save(loaded);
        Assert.Equal(4, Scalar(o, WorkFamily.CraftedUnits));
        Assert.Equal(2, Scalar(o, WorkDetailsFamily.CraftedByDef));
        Assert.Equal(MetricCoverage.Partial, o.Scalar(WorkDetailsFamily.CraftedByDef, 1)!.Coverage);
    }

    [Fact]
    public void PerDefOverflow_OnlyWeakensChildrenAndNeverDropsTheIndependentTotal()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { WorkDetailsFamily.Family.Spec }, 0);
        for (int i = 0; i <= FamilyOps.KeyCap; i++)
            WorkDetailsFamily.CraftedProducts(a.Family(WorkDetailsFamily.Name)!, new[] { Product("Def_" + i, 1) });
        var observation = Save(a);
        var entry = observation.Metrics[WorkDetailsFamily.CraftedByDef];
        var total = observation.Scalar(WorkDetailsFamily.CraftedByDef, 1)!;
        Assert.Equal(FamilyOps.KeyCap + 1, total.IntegerValue);
        Assert.Equal(MetricCoverage.Complete, total.Coverage);
        var children = entry.Values.Where(x => x.Key != MetricEntry.Scalar).Select(x => x.Value).ToArray();
        Assert.Equal(FamilyOps.KeyCap, children.Length);
        Assert.All(children, child => Assert.Equal(MetricCoverage.Partial, child.Coverage));
    }

    [Fact]
    public void NewUnit_IsUnverifiedAndCataloged()
    {
        Assert.Equal(AcquisitionStatus.Unverified, AcquisitionUnits.Find(WorkDetailsFamily.Name)!.Status);
        foreach (var key in new[] { WorkDetailsFamily.CraftedByDef, WorkDetailsFamily.MinedByDef })
        {
            var definition = MetricRegistry.Catalog.Single(d => d.Key == key);
            Assert.True(definition.HasBreakdown);
            Assert.False(definition.Published);
            Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(definition));
        }
    }
}
