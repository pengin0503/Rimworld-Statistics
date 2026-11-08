namespace RimworldStatistics.Core.Tests;

/// <summary>Biotech Core never infers completed events from mere job starts, loaded pawns or item loss.</summary>
public sealed class NumericBiotechEventsTests
{
    private static MetricObservation Save(LiveMetricAccumulator a)
    {
        var d = a.PrepareSave(60000, DateTime.UtcNow, "btech")!;
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "btech" }, null, null);
    }

    [Fact]
    public void UniqueBirthMothersProducedMechsAndGrowthMilestones()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { BiotechFamily.Family.Spec }, 0);
        var s = a.Family(BiotechFamily.Name)!;
        BiotechFamily.MechProduced(s, "m1", true, true);
        BiotechFamily.MechProduced(s, "m1", true, true);
        BiotechFamily.MechProduced(s, "m2", false, true);
        BiotechFamily.BirthConfirmed(s, "mom1", true, true);
        BiotechFamily.BirthConfirmed(s, "mom1", true, true);
        BiotechFamily.BirthConfirmed(s, "mom2", false, true);
        BiotechFamily.ChildReachedStage(s, "kid", "Child", true, true);
        BiotechFamily.ChildReachedStage(s, "kid", "Child", true, true);
        BiotechFamily.ChildReachedStage(s, "kid", "Adult", true, true);
        BiotechFamily.ChildReachedStage(s, "npc", "Child", false, true);
        var o = Save(a);
        Assert.Equal(1, o.Scalar(BiotechFamily.MechsCreated, MetricKeys.Revision(BiotechFamily.MechsCreated))!.IntegerValue);
        Assert.Equal(1, o.Scalar(BiotechFamily.BirthMothers, MetricKeys.Revision(BiotechFamily.BirthMothers))!.IntegerValue);
        Assert.Equal(2, o.Scalar(BiotechFamily.ChildStages, MetricKeys.Revision(BiotechFamily.ChildStages))!.IntegerValue);
        // The catalog promises a stage breakdown, not just the scalar count.
        var stages = o.Metrics[BiotechFamily.ChildStages].Values;
        Assert.Equal(1, stages["Child"].IntegerValue);
        Assert.Equal(1, stages["Adult"].IntegerValue);
        Assert.Equal(2, stages[MetricEntry.Scalar].IntegerValue);
    }

    [Fact]
    public void RepairDisposalAndCleanupRequireCompletionEvidence()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { BiotechFamily.Family.Spec }, 0);
        var s = a.Family(BiotechFamily.Name)!;
        BiotechFamily.MechFullyRepaired(s, "op1", true, false);
        BiotechFamily.MechFullyRepaired(s, "op1", true, true);
        BiotechFamily.MechFullyRepaired(s, "op1", true, true);
        BiotechFamily.WastePackProduced(s, 10, true);
        BiotechFamily.WastePackProduced(s, 100, false);
        BiotechFamily.WastePackNeutralized(s, 2, false);
        BiotechFamily.WastePackNeutralized(s, 3, true);
        BiotechFamily.PollutionCellCleaned(s, "clean1", true, true, false, true);
        BiotechFamily.PollutionCellCleaned(s, "clean1", true, true, false, true);
        BiotechFamily.PollutionCellCleaned(s, "clean2", true, false, false, true);
        BiotechFamily.PollutionCellCleaned(s, "clean3", false, true, false, true);
        var o = Save(a);
        Assert.Equal(1, o.Scalar(BiotechFamily.MechRepairs, MetricKeys.Revision(BiotechFamily.MechRepairs))!.IntegerValue);
        Assert.Equal(10, o.Scalar(BiotechFamily.WasteCreated, MetricKeys.Revision(BiotechFamily.WasteCreated))!.IntegerValue);
        Assert.Equal(3, o.Scalar(BiotechFamily.WasteDisposed, MetricKeys.Revision(BiotechFamily.WasteDisposed))!.IntegerValue);
        Assert.Equal(1, o.Scalar(BiotechFamily.PollutionCleaned, MetricKeys.Revision(BiotechFamily.PollutionCleaned))!.IntegerValue);
    }

    [Fact]
    public void BiotechEventOutputsStayDlcGatedAndUnverified()
    {
        var d = MetricRegistry.Find(BiotechFamily.PollutionCleaned)!;
        Assert.Equal(AcquisitionUnits.Biotech, d.RequiredDlc);
        Assert.False(d.Published);
    }
}
