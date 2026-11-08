namespace RimworldStatistics.Core.Tests;

public sealed class NumericRoyaltyEventsTests
{
    [Fact]
    public void OnlyPlayerConfirmedPermitAndSuccessfulFreeColonistPsycastsCount()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { RoyaltyFamily.Family.Spec }, 0);
        var s = a.Family(RoyaltyFamily.Name)!;
        RoyaltyFamily.PermitUsed(s, playerOwned: false, confirmed: true);
        RoyaltyFamily.PermitUsed(s, playerOwned: true, confirmed: false);
        RoyaltyFamily.PermitUsed(s, playerOwned: true, confirmed: true);
        RoyaltyFamily.PermitUsed(s, playerOwned: true, confirmed: true);

        RoyaltyFamily.PsycastSucceeded(s, playerFreeColonist: true, confirmed: false);
        RoyaltyFamily.PsycastSucceeded(s, playerFreeColonist: false, confirmed: true);
        RoyaltyFamily.PsycastSucceeded(s, playerFreeColonist: true, confirmed: true);
        var d = a.PrepareSave(60000, DateTime.UtcNow, "royalty-event")!;
        var o = CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "royalty-event" }, null, null);
        Assert.Equal(2, o.Scalar(RoyaltyFamily.PermitUses, MetricKeys.Revision(RoyaltyFamily.PermitUses))!.IntegerValue);
        Assert.Equal(1, o.Scalar(RoyaltyFamily.Psycasts, MetricKeys.Revision(RoyaltyFamily.Psycasts))!.IntegerValue);
        Assert.False(MetricRegistry.Find(RoyaltyFamily.Psycasts)!.Published);
    }
}
