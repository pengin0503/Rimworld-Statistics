namespace RimworldStatistics.Core.Tests;

/// <summary>Odyssey flight algebra can be unit-tested without unsafe guesses about the game landing API.</summary>
public sealed class NumericOdysseyTests
{
    private static MetricObservation Observe(LiveMetricAccumulator a)
    {
        var draft = a.PrepareSave(60000, DateTime.UtcNow, "odyssey")!;
        return CheckpointMetrics.ToObservation(draft.Payload!, draft.Json,
            new ColonyIdentity { ModCampaignId = "odyssey" }, null, null);
    }

    [Fact]
    public void OneCompletedFlightIsCountedOnceAndArrivalIsLayerScoped()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { OdysseyFamily.Family.Spec }, 0);
        var s = a.Family(OdysseyFamily.Name)!;
        OdysseyFamily.FlightCompleted(s, "flight1", 0, 100, true, true, 18, true);
        OdysseyFamily.FlightCompleted(s, "flight1", 0, 100, true, true, 18, true);
        OdysseyFamily.FlightCompleted(s, "flight2", 0, 100, true, true, 24, false);
        OdysseyFamily.FlightCompleted(s, "flight3", 1, 100, true, true, 7, true);
        OdysseyFamily.FlightCompleted(s, "cancel", 1, 300, true, false, 200, true);
        OdysseyFamily.FlightCompleted(s, "npc", 0, 1000, false, true, 500, true);
        var o = Observe(a);
        Assert.Equal(3, o.Scalar(OdysseyFamily.Flights, MetricKeys.Revision(OdysseyFamily.Flights))!.IntegerValue);
        Assert.Equal(2, o.Scalar(OdysseyFamily.UniqueTiles, MetricKeys.Revision(OdysseyFamily.UniqueTiles))!.IntegerValue);
        Assert.Equal(2, o.Scalar(OdysseyFamily.NewMapLandings, MetricKeys.Revision(OdysseyFamily.NewMapLandings))!.IntegerValue);
        Assert.Equal(49d, o.Scalar(OdysseyFamily.Distance, MetricKeys.Revision(OdysseyFamily.Distance))!.RealValue);
    }

    [Fact]
    public void MissingAuthoritativeDistanceNeverBecomesACompleteZero()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { OdysseyFamily.Family.Spec }, 0);
        var s = a.Family(OdysseyFamily.Name)!;
        OdysseyFamily.FlightCompleted(s, "f", 0, 1, true, true, null, null);
        var o = Observe(a);
        Assert.Equal(1, o.Scalar(OdysseyFamily.Flights, MetricKeys.Revision(OdysseyFamily.Flights))!.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, o.Scalar(OdysseyFamily.Distance, MetricKeys.Revision(OdysseyFamily.Distance))!.Coverage);
        Assert.Equal(MetricCoverage.Partial, o.Scalar(OdysseyFamily.NewMapLandings, MetricKeys.Revision(OdysseyFamily.NewMapLandings))!.Coverage);
    }

    [Fact]
    public void OdysseyOutputsRemainUnpublishedWithTheDlcGateClosed()
    {
        var d = MetricRegistry.Find(OdysseyFamily.Flights)!;
        Assert.Equal(AcquisitionUnits.Odyssey, d.RequiredDlc);
        Assert.False(d.Published);
        Assert.Equal(AcquisitionStatus.Unverified, AcquisitionUnits.Find(OdysseyFamily.Name)!.Status);
    }
}
