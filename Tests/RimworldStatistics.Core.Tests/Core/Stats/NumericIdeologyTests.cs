namespace RimworldStatistics.Core.Tests;

public sealed class NumericIdeologyTests
{
    [Fact]
    public void ConfirmedPlayerConversionsExcludeInitialAndRepeatedIdeology()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { IdeologyFamily.Family.Spec }, 0);
        var s = a.Family(IdeologyFamily.Name)!;
        IdeologyFamily.Converted(s, "colonist", null, "IdeoA", true, true);
        IdeologyFamily.Converted(s, "colonist", "IdeoA", "IdeoA", true, true);
        IdeologyFamily.Converted(s, "raider", "IdeoA", "IdeoB", false, true);
        IdeologyFamily.Converted(s, "newPawn", "IdeoA", "IdeoB", true, false);
        IdeologyFamily.Converted(s, "colonist", "IdeoA", "IdeoB", true, true);
        IdeologyFamily.Converted(s, "prisoner", "IdeoB", "IdeoC", true, true);
        var d = a.PrepareSave(60000, DateTime.UtcNow, "ideo")!;
        var o = CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "ideo" }, null, null);
        Assert.Equal(2, o.Scalar(IdeologyFamily.Conversions, MetricKeys.Revision(IdeologyFamily.Conversions))!.IntegerValue);
    }

    [Fact]
    public void IdeologyMetricRemainsUnverifiedAndDlcGated()
    {
        var d = MetricRegistry.Find(IdeologyFamily.Conversions)!;
        Assert.Equal("ideology", d.Acquisition);
        Assert.Equal(AcquisitionUnits.Ideology, d.RequiredDlc);
        Assert.False(d.Published);
    }
}
