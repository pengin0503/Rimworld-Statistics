namespace RimworldStatistics.Core.Tests;

public sealed class ChoreLifetimeTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    private static MetricObservation Save(LiveMetricAccumulator a, int tick)
    {
        var draft = a.PrepareSave(tick, T0, "C" + tick)!;
        a.Commit(draft);
        return CheckpointMetrics.ToObservation(draft.Payload!, draft.Json, new ColonyIdentity { ModCampaignId = "chore" }, null, null);
    }

    private static MetricValue Read(MetricObservation o, string key) => o.Scalar(key, MetricKeys.Revision(key))!;

    [Fact]
    public void SuccessfulRecordEvents_CountOneEach_NoSharedWorkCounter()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { ChoreFamily.Family.Spec }, 0);
        var state = acc.Family(ChoreFamily.Name)!;
        ChoreFamily.FilthCleared(state);
        ChoreFamily.FilthCleared(state);
        ChoreFamily.FireCleared(state);
        ChoreFamily.MealIngested(state);
        var obs = Save(acc, 200);
        Assert.Equal(2, Read(obs, ChoreFamily.FilthRemoved).IntegerValue);
        Assert.Equal(1, Read(obs, ChoreFamily.FiresExtinguished).IntegerValue);
        Assert.Equal(1, Read(obs, ChoreFamily.MealIngestions).IntegerValue);
        Assert.False(obs.Metrics.ContainsKey(WorkFamily.CraftedUnits));
        Assert.Equal(MetricCoverage.Complete, Read(obs, ChoreFamily.FilthRemoved).Coverage);
    }

    [Fact]
    public void ResumeCheckpoint_KeepsCountsAndDoesNotBackfillOldUnknownTime()
    {
        var first = LiveMetricAccumulator.StartNew(new[] { ChoreFamily.Family.Spec }, 0);
        ChoreFamily.FireCleared(first.Family(ChoreFamily.Name)!);
        var d = first.PrepareSave(100, T0, "before")!;
        first.Commit(d);
        var resumed = LiveMetricAccumulator.Resume(d.Json, new[] { ChoreFamily.Family.Spec }, 100);
        ChoreFamily.FilthCleared(resumed.Family(ChoreFamily.Name)!);
        var obs = Save(resumed, 200);
        Assert.Equal(1, Read(obs, ChoreFamily.FiresExtinguished).IntegerValue);
        Assert.Equal(1, Read(obs, ChoreFamily.FilthRemoved).IntegerValue);

        var late = LiveMetricAccumulator.Resume(null, new[] { ChoreFamily.Family.Spec }, 100);
        ChoreFamily.FireCleared(late.Family(ChoreFamily.Name)!);
        var partial = Save(late, 200);
        Assert.Equal(1, Read(partial, ChoreFamily.FiresExtinguished).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, Read(partial, ChoreFamily.FiresExtinguished).Coverage);
    }

    [Fact]
    public void ChoreRecording_IsOptInAndLifetimeSemanticsExplicit()
    {
        var unit = AcquisitionUnits.Find(ChoreFamily.Name)!;
        Assert.Equal(AcquisitionStatus.Unverified, unit.Status);
        Assert.Equal(AcquisitionKind.Family, unit.Kind);
        foreach (var key in new[] { ChoreFamily.FilthRemoved, ChoreFamily.FiresExtinguished, ChoreFamily.MealIngestions })
        {
            var def = MetricRegistry.Catalog.Single(x => x.Key == key);
            Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(def));
            Assert.False(def.Published);
            Assert.Equal(OverallAggregation.Sum, def.Overall);
        }
    }
}
