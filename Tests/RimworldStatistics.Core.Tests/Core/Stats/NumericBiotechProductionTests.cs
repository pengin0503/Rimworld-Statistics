namespace RimworldStatistics.Core.Tests;

public sealed class NumericBiotechProductionTests
{
    [Fact]
    public void ManufactureRequiresPlayerCompletedGestationAndRealSpawn()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { BiotechFamily.Family.Spec }, 0);
        var s = a.Family(BiotechFamily.Name)!;
        BiotechFamily.GestationReady(s, "mechA", playerGestator: false, resurrection: false);
        BiotechFamily.GestationEjected(s, "mechA", livingPlayerMech: true);
        BiotechFamily.GestationReady(s, "resurrect", playerGestator: true, resurrection: true);
        BiotechFamily.GestationEjected(s, "resurrect", livingPlayerMech: true);
        BiotechFamily.GestationReady(s, "mechB", playerGestator: true, resurrection: false);
        BiotechFamily.GestationEjected(s, "mechB", livingPlayerMech: false); // still in tank
        BiotechFamily.GestationEjected(s, "mechB", livingPlayerMech: true);
        BiotechFamily.GestationEjected(s, "mechB", livingPlayerMech: true); // duplicate postfix
        BiotechFamily.GestationReady(s, "mechC", playerGestator: true, resurrection: false);
        BiotechFamily.GestationEjected(s, "mechC", livingPlayerMech: true);
        var draft = a.PrepareSave(60000, DateTime.UtcNow, "m")!;
        var o = CheckpointMetrics.ToObservation(draft.Payload!, draft.Json,
            new ColonyIdentity { ModCampaignId = "test" }, null, null);
        Assert.Equal(2, o.Scalar(BiotechFamily.MechsCreated, MetricKeys.Revision(BiotechFamily.MechsCreated))!.IntegerValue);
    }
}
