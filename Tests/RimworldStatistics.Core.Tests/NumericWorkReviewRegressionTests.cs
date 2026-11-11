namespace RimworldStatistics.Core.Tests;

/// <summary>Regressions for the Phase C work-family review on PR #26.</summary>
public sealed class NumericWorkReviewRegressionTests
{
    private const int Day = 60000;

    private static MetricValue At(LiveMetricAccumulator acc, int tick, string key)
    {
        var draft = acc.PrepareSave(tick, new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc), "verify-" + tick)!;
        var obs = CheckpointMetrics.ToObservation(draft.Payload!, draft.Json,
            new ColonyIdentity { ModCampaignId = "work-review" }, null, null);
        return obs.Scalar(key, MetricKeys.Revision(key))!;
    }

    [Fact]
    public void FullyObservedPeriodsWithoutCraftingOrHarvestingHaveMeasuredZeroPeaks()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { WorkFamily.Family.Spec }, 0);
        Assert.Equal(MetricCoverage.NotApplicable, At(acc, Day / 2, WorkFamily.DailyPeak).Coverage);
        var daily = At(acc, Day, WorkFamily.DailyPeak);
        Assert.Equal(0, daily.IntegerValue);
        Assert.Equal(MetricCoverage.Complete, daily.Coverage);
        var annual = At(acc, 60 * Day, WorkFamily.HarvestYearPeak);
        Assert.Equal(0, annual.IntegerValue);
        Assert.Equal(MetricCoverage.Complete, annual.Coverage);
    }

    [Fact]
    public void APartialFirstPeriodDoesNotBecomeAFakeZeroPeriod()
    {
        var acc = LiveMetricAccumulator.Resume(null, new[] { WorkFamily.Family.Spec }, Day / 2);
        Assert.Equal(MetricCoverage.NotApplicable, At(acc, Day + Day / 2, WorkFamily.DailyPeak).Coverage);
        var completeDay = At(acc, 2 * Day, WorkFamily.DailyPeak);
        Assert.Equal(0, completeDay.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, completeDay.Coverage);
    }

    [Fact]
    public void SownAndHarvestAreRetained_WithoutRetiredCropLossCounters()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { WorkFamily.Family.Spec }, 0);
        var state = acc.Family(WorkFamily.Name)!;
        WorkFamily.Sown(state, "Plant_Rice");
        WorkFamily.Harvested(state, "Thing_Worker", "Thing_Plant", "Plant_Rice", 3, 1);
        Assert.Equal(1, At(acc, Day, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(3, At(acc, Day, WorkFamily.HarvestUnits).IntegerValue);
        Assert.Null(MetricRegistry.Find("work.crops.cold_loss"));
        Assert.Null(MetricRegistry.Find("work.crops.other_loss"));
        Assert.Null(MetricRegistry.Find("work.crops.loss_rate"));
    }
}
