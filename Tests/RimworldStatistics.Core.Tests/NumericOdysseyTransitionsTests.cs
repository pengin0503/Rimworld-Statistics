namespace RimworldStatistics.Core.Tests;

public sealed class NumericOdysseyTransitionsTests
{
    [Fact]
    public void OnlyLandedFlightsWithObservedTakeoffCount_DuplicateLandingsAreIgnored()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { OdysseyFamily.Family.Spec }, 0);
        var s = a.Family(OdysseyFamily.Name)!;
        OdysseyFamily.ObservedTakeoff(s, "engine1", 100, playerShip: false);
        OdysseyFamily.ObservedLanding(s, "engine1", 0, 50, playerShip: true,
            successfullyLanded: true, routeDistanceInTiles: 5, landedOnNewMap: true);
        OdysseyFamily.ObservedTakeoff(s, "engine1", 120, playerShip: true);
        OdysseyFamily.ObservedLanding(s, "engine1", 0, 50, playerShip: true,
            successfullyLanded: false, routeDistanceInTiles: 5, landedOnNewMap: true);
        OdysseyFamily.ObservedLanding(s, "engine1", 0, 50, playerShip: true,
            successfullyLanded: true, routeDistanceInTiles: 5, landedOnNewMap: true);
        OdysseyFamily.ObservedLanding(s, "engine1", 0, 50, playerShip: true,
            successfullyLanded: true, routeDistanceInTiles: 5, landedOnNewMap: true);
        OdysseyFamily.ObservedTakeoff(s, "engine1", 200, playerShip: true);
        OdysseyFamily.ObservedLanding(s, "engine1", 0, 50, playerShip: true,
            successfullyLanded: true, routeDistanceInTiles: 7, landedOnNewMap: false);
        var d = a.PrepareSave(300, DateTime.UtcNow, "o")!;
        var o = CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "o" }, null, null);
        Assert.Equal(2, o.Scalar(OdysseyFamily.Flights, MetricKeys.Revision(OdysseyFamily.Flights))!.IntegerValue);
        Assert.Equal(1, o.Scalar(OdysseyFamily.UniqueTiles, MetricKeys.Revision(OdysseyFamily.UniqueTiles))!.IntegerValue);
        Assert.Equal(12d, o.Scalar(OdysseyFamily.Distance, MetricKeys.Revision(OdysseyFamily.Distance))!.Number);
        Assert.Equal(1, o.Scalar(OdysseyFamily.NewMapLandings, MetricKeys.Revision(OdysseyFamily.NewMapLandings))!.IntegerValue);
    }
}
