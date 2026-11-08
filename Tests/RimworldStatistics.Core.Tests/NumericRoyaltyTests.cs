namespace RimworldStatistics.Core.Tests;

public sealed class NumericRoyaltyTests
{
    [Fact]
    public void OnlyConfirmedUpwardTitleChangesCountAndHighestDefIsRetained()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { RoyaltyFamily.Family.Spec }, 0);
        var s = a.Family(RoyaltyFamily.Name)!;
        RoyaltyFamily.TitleChanged(s, "Yeoman", null, 1, true);
        RoyaltyFamily.TitleChanged(s, "Yeoman", 1, 1, true);
        RoyaltyFamily.TitleChanged(s, "Count", 1, 5, true);
        RoyaltyFamily.TitleChanged(s, "Acolyte", 5, 2, true);
        RoyaltyFamily.TitleChanged(s, "Baron", 2, 4, false);
        var d = a.PrepareSave(60000, DateTime.UtcNow, "royalty")!;
        var o = CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "royalty" }, null, null);
        Assert.Equal(2, o.Scalar(RoyaltyFamily.TitleGrants, MetricKeys.Revision(RoyaltyFamily.TitleGrants))!.IntegerValue);
        Assert.Equal(5, o.Scalar(RoyaltyFamily.HighestTitle, MetricKeys.Revision(RoyaltyFamily.HighestTitle))!.IntegerValue);
        Assert.Contains("Count", o.Metrics[RoyaltyFamily.HighestTitle].Values.Keys);
    }

    [Fact]
    public void HonorDoesNotTreatOrdinaryLossAsConfirmedSpent()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { RoyaltyFamily.Family.Spec }, 0);
        var s = a.Family(RoyaltyFamily.Name)!;
        RoyaltyFamily.HonorChanged(s, 5, 17, true, false);
        RoyaltyFamily.HonorChanged(s, 17, 12, true, true);
        RoyaltyFamily.HonorChanged(s, 12, 5, true, false);
        RoyaltyFamily.HonorChanged(s, 5, 100, false, false);
        var d = a.PrepareSave(1, DateTime.UtcNow, "royalty")!;
        var o = CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "royalty" }, null, null);
        Assert.Equal(12, o.Metrics[RoyaltyFamily.Honor].Values["gained"].IntegerValue);
        Assert.Equal(5, o.Metrics[RoyaltyFamily.Honor].Values["spent"].IntegerValue);
    }

    [Fact]
    public void UnitIsDlcGatedAndUnpublished()
    {
        var d = MetricRegistry.Find(RoyaltyFamily.TitleGrants)!;
        Assert.Equal(AcquisitionUnits.Royalty, d.RequiredDlc);
        Assert.Equal("royalty", d.Acquisition);
        Assert.False(d.Published);
    }
}
