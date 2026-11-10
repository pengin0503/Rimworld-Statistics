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
        ChoreFamily.CleanedWithinJob(state, "Job_001");
        ChoreFamily.CleanedWithinJob(state, "Job_001");
        ChoreFamily.CleanedWithinJob(state, "Job_002");
        ChoreFamily.CleaningJobEnded(state, "Job_001", succeeded: true);
        ChoreFamily.CleaningJobEnded(state, "Job_002", succeeded: false);
        ChoreFamily.HaulDeposited(state, 5);
        ChoreFamily.HaulDeposited(state, 3);
        ChoreFamily.HaulDeposited(state, 0);
        ChoreFamily.WoodObtained(state, 7);
        ChoreFamily.WoodObtained(state, 0);
        ChoreFamily.WoodObtained(state, 3);
        ChoreFamily.MarkSlaughteredCorpse(state, "Corpse_001");
        ChoreFamily.MarkSlaughteredCorpse(state, "Corpse_002");
        ChoreFamily.CorpseConsumed(state, "Corpse_001", butcherFleshRecipe: true);
        ChoreFamily.CorpseConsumed(state, "Corpse_001", butcherFleshRecipe: true);
        ChoreFamily.CorpseConsumed(state, "Corpse_002", butcherFleshRecipe: false);
        var obs = Save(acc, 200);
        Assert.Equal(2, Read(obs, ChoreFamily.FilthRemoved).IntegerValue);
        Assert.Equal(1, Read(obs, ChoreFamily.CleaningJobsCompleted).IntegerValue);
        Assert.Equal(1, Read(obs, ChoreFamily.FiresExtinguished).IntegerValue);
        Assert.Equal(1, Read(obs, ChoreFamily.MealIngestions).IntegerValue);
        Assert.Equal(2, Read(obs, ChoreFamily.HaulPlacements).IntegerValue);
        Assert.Equal(8, Read(obs, ChoreFamily.HauledUnits).IntegerValue);
        Assert.Equal(10, Read(obs, ChoreFamily.TreeWoodPlaced).IntegerValue);
        Assert.Equal(1, Read(obs, ChoreFamily.SlaughteredButchery).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, Read(obs, ChoreFamily.SlaughteredButchery).Coverage);
        Assert.Equal(MetricCoverage.Partial, Read(obs, ChoreFamily.TreeWoodPlaced).Coverage);
        Assert.Equal(MetricCoverage.Partial, Read(obs, ChoreFamily.HauledUnits).Coverage);
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
    public void CompletedWorkLineages_SurviveSaveLoadAndRejectDuplicateOrInvalidBodies()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { ChoreFamily.Family.Spec }, 0);
        var state = acc.Family(ChoreFamily.Name)!;
        ChoreFamily.MarkSlaughteredCorpse(state, "Corpse_Slaughtered");
        ChoreFamily.MarkSlaughteredCorpse(state, "Corpse_NotButchered");
        ChoreFamily.MarkSlaughteredCorpse(state, "Corpse_Slaughtered");
        ChoreFamily.CleanedWithinJob(state, "Job_Cleaned");
        ChoreFamily.CleanedWithinJob(state, "Job_Interrupted");
        var draft = acc.PrepareSave(100, T0, "checkpoint")!;
        acc.Commit(draft);

        var resumed = LiveMetricAccumulator.Resume(draft.Json, new[] { ChoreFamily.Family.Spec }, 100);
        var loaded = resumed.Family(ChoreFamily.Name)!;
        ChoreFamily.CorpseConsumed(loaded, "Corpse_Slaughtered", true);
        ChoreFamily.CorpseConsumed(loaded, "Corpse_Slaughtered", true);
        ChoreFamily.CorpseConsumed(loaded, "Corpse_NeverMarked", true);
        ChoreFamily.CorpseConsumed(loaded, "Corpse_NotButchered", false);
        ChoreFamily.CleaningJobEnded(loaded, "Job_Cleaned", true);
        ChoreFamily.CleaningJobEnded(loaded, "Job_Cleaned", true);
        ChoreFamily.CleaningJobEnded(loaded, "Job_Interrupted", false);
        var observation = Save(resumed, 200);
        Assert.Equal(1, Read(observation, ChoreFamily.SlaughteredButchery).IntegerValue);
        Assert.Equal(1, Read(observation, ChoreFamily.CleaningJobsCompleted).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, Read(observation, ChoreFamily.SlaughteredButchery).Coverage);
        Assert.Empty(loaded.Active);
    }

    [Fact]
    public void UnverifiedCorpusTrackerCapacity_OnlyWeakensCompletionNotOtherChoreCounters()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { ChoreFamily.Family.Spec }, 0);
        var state = acc.Family(ChoreFamily.Name)!;
        for (int i = 0; i <= FamilyOps.KeyCap; i++)
            ChoreFamily.MarkSlaughteredCorpse(state, "Corpse_" + i);
        ChoreFamily.FireCleared(state);
        ChoreFamily.CorpseConsumed(state, "Corpse_0", true);
        ChoreFamily.CorpseConsumed(state, "Corpse_" + FamilyOps.KeyCap, true);
        var observation = Save(acc, 100);
        Assert.Equal(1, Read(observation, ChoreFamily.SlaughteredButchery).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, Read(observation, ChoreFamily.SlaughteredButchery).Coverage);
        Assert.Equal(1, Read(observation, ChoreFamily.FiresExtinguished).IntegerValue);
        Assert.Equal(MetricCoverage.Complete, Read(observation, ChoreFamily.FiresExtinguished).Coverage);
    }

    [Fact]
    public void ChoreRecording_IsOptInAndLifetimeSemanticsExplicit()
    {
        var unit = AcquisitionUnits.Find(ChoreFamily.Name)!;
        Assert.Equal(AcquisitionStatus.Unverified, unit.Status);
        Assert.Equal(AcquisitionKind.Family, unit.Kind);
        foreach (var key in new[] { ChoreFamily.FilthRemoved, ChoreFamily.CleaningJobsCompleted, ChoreFamily.FiresExtinguished, ChoreFamily.MealIngestions, ChoreFamily.HaulPlacements, ChoreFamily.HauledUnits, ChoreFamily.TreeWoodPlaced, ChoreFamily.SlaughteredButchery })
        {
            var def = MetricRegistry.Catalog.Single(x => x.Key == key);
            Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(def));
            Assert.False(def.Published);
            Assert.Equal(OverallAggregation.Sum, def.Overall);
        }
    }

    [Fact]
    public void DestroyedCorpse_IsReleasedAfterRecipeTickWithoutLosingLegitimateButchery()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { ChoreFamily.Family.Spec }, 0);
        var state = acc.Family(ChoreFamily.Name)!;
        ChoreFamily.MarkSlaughteredCorpse(state, "processed", 100);
        ChoreFamily.CorpseDestroyed(state, "processed", 101);
        // Vanilla consumes ingredients before notifying the completed butcher bill in this same tick.
        ChoreFamily.PruneDestroyedCorpses(state, 101);
        ChoreFamily.CorpseConsumed(state, "processed", true);
        ChoreFamily.PruneDestroyedCorpses(state, 102);

        ChoreFamily.MarkSlaughteredCorpse(state, "rotted", 110);
        ChoreFamily.CorpseDestroyed(state, "rotted", 120);
        ChoreFamily.PruneDestroyedCorpses(state, 121);
        Assert.Null(state.Tracked("slaughter_corpses", "rotted"));
        ChoreFamily.CorpseConsumed(state, "rotted", true);

        for (int i = 0; i < FamilyOps.KeyCap + 20; i++)
        {
            string id = "decayed_" + i;
            int tick = 130 + i * 2;
            ChoreFamily.MarkSlaughteredCorpse(state, id, tick);
            ChoreFamily.CorpseDestroyed(state, id, tick);
            ChoreFamily.PruneDestroyedCorpses(state, tick + 1);
        }
        Assert.False(state.TrackerOverflowed("slaughter_corpses"));
        Assert.Empty(state.Active);
        var observed = Save(acc, 10000);
        Assert.Equal(1L, Read(observed, ChoreFamily.SlaughteredButchery).IntegerValue);
    }


    [Fact]
    public void PendingDestroyedCorpseMarker_SurvivesReloadUntilNextTick()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { ChoreFamily.Family.Spec }, 0);
        var state = acc.Family(ChoreFamily.Name)!;
        ChoreFamily.MarkSlaughteredCorpse(state, "pending", 50);
        ChoreFamily.CorpseDestroyed(state, "pending", 100);
        var draft = acc.PrepareSave(100, T0, "destroyed-just-before-save")!;
        acc.Commit(draft);

        var resumed = LiveMetricAccumulator.Resume(draft.Json, new[] { ChoreFamily.Family.Spec }, 100);
        var loaded = resumed.Family(ChoreFamily.Name)!;
        Assert.NotNull(loaded.Tracked("slaughter_corpses", "pending"));
        ChoreFamily.CorpseConsumed(loaded, "pending", butcherFleshRecipe: true);
        ChoreFamily.PruneDestroyedCorpses(loaded, 101);
        Assert.Empty(loaded.Active);
        Assert.Equal(1L, Read(Save(resumed, 101), ChoreFamily.SlaughteredButchery).IntegerValue);

        var second = LiveMetricAccumulator.StartNew(new[] { ChoreFamily.Family.Spec }, 0);
        var target = second.Family(ChoreFamily.Name)!;
        ChoreFamily.MarkSlaughteredCorpse(target, "unprocessed", 100);
        ChoreFamily.CorpseDestroyed(target, "unprocessed", 100);
        var snap = second.PrepareSave(100, T0, "destroyed-not-butchered")!;
        var later = LiveMetricAccumulator.Resume(snap.Json, new[] { ChoreFamily.Family.Spec }, 100);
        ChoreFamily.PruneDestroyedCorpses(later.Family(ChoreFamily.Name)!, 101);
        Assert.Empty(later.Family(ChoreFamily.Name)!.Active);
    }

}
