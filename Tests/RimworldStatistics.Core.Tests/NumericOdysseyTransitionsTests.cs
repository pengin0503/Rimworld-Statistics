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
    [Fact]
    public void UnloadedExistingMapMustNotBeInventedAsNewWithoutGenerationEvidence()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { OdysseyFamily.Family.Spec }, 0);
        var s = a.Family(OdysseyFamily.Name)!;
        // The preflight list is only loaded maps; an old world map may be absent.
        OdysseyFamily.ObservedExistingMaps(s, new[] { "Map_101" });
        OdysseyFamily.ObservedTakeoff(s, "engine", 100, playerShip: true);
        OdysseyFamily.ObservedLanding(s, "engine", 0, 25,
            playerShip: true, successfullyLanded: true, routeDistanceInTiles: null,
            landedOnNewMap: null, arrivedMapId: "Map_999");
        var draft = a.PrepareSave(200, DateTime.UtcNow, "unknown-map")!;
        var observation = CheckpointMetrics.ToObservation(draft.Payload!, draft.Json,
            new ColonyIdentity { ModCampaignId = "unknown-map" }, null, null);
        var newLandings = observation.Scalar(OdysseyFamily.NewMapLandings,
            MetricKeys.Revision(OdysseyFamily.NewMapLandings))!;
        Assert.Equal(0, newLandings.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, newLandings.Coverage);
        Assert.Equal(1, observation.Scalar(OdysseyFamily.Flights,
            MetricKeys.Revision(OdysseyFamily.Flights))!.IntegerValue);
    }

    [Fact]
    public void NewMapClassificationUsesPersistedPreflightMapIdsAcrossReloads()
    {
        var s = LiveMetricAccumulator.StartNew(new[] { OdysseyFamily.Family.Spec }, 0);
        var state = s.Family(OdysseyFamily.Name)!;
        OdysseyFamily.ObservedExistingMaps(state, new[] { "Map_101", "Map_102" });
        OdysseyFamily.ObservedTakeoff(state, "engine", 100, playerShip: true);
        OdysseyFamily.ObservedMapGenerated(state, "engine", "Map_103", 150, confirmed: true);
        OdysseyFamily.ObservedLanding(state, "engine", 0, 25,
            playerShip: true, successfullyLanded: true, routeDistanceInTiles: null,
            landedOnNewMap: null, arrivedMapId: "Map_103");
        OdysseyFamily.ObservedTakeoff(state, "engine", 200, playerShip: true);
        OdysseyFamily.ObservedLanding(state, "engine", 0, 26,
            playerShip: true, successfullyLanded: true, routeDistanceInTiles: null,
            landedOnNewMap: null, arrivedMapId: "Map_102"); // existing map
        OdysseyFamily.ObservedTakeoff(state, "engine", 300, playerShip: true);
        OdysseyFamily.ObservedLanding(state, "engine", 0, 27,
            playerShip: true, successfullyLanded: true, routeDistanceInTiles: null,
            landedOnNewMap: null, arrivedMapId: "Map_103"); // previously visited
        var d = s.PrepareSave(400, DateTime.UtcNow, "route")!;
        var o = CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "route" }, null, null);
        Assert.Equal(3, o.Scalar(OdysseyFamily.Flights, MetricKeys.Revision(OdysseyFamily.Flights))!.IntegerValue);
        Assert.Equal(1, o.Scalar(OdysseyFamily.NewMapLandings, MetricKeys.Revision(OdysseyFamily.NewMapLandings))!.IntegerValue);
        Assert.Equal(MetricCoverage.Partial,
            o.Scalar(OdysseyFamily.Distance, MetricKeys.Revision(OdysseyFamily.Distance))!.Coverage);
    }

    [Fact]
    public void NewMapGenerationProofSurvivesReloadButCannotBeReusedForASecondFlight()
    {
        var original = LiveMetricAccumulator.StartNew(new[] { OdysseyFamily.Family.Spec }, 0);
        var state = original.Family(OdysseyFamily.Name)!;
        OdysseyFamily.ObservedExistingMaps(state, new[] { "Map_101" });
        // A map created before the departure is not evidence for that flight.
        OdysseyFamily.ObservedMapGenerated(state, "engine", "Map_103", 90, confirmed: true);
        OdysseyFamily.ObservedTakeoff(state, "engine", 100, playerShip: true);
        OdysseyFamily.ObservedMapGenerated(state, "engine", "Map_103", 150, confirmed: true);
        var draft = original.PrepareSave(160, DateTime.UtcNow, "midflight")!;
        var resumed = LiveMetricAccumulator.Resume(draft.Json, new[] { OdysseyFamily.Family.Spec }, 160);
        var restored = resumed.Family(OdysseyFamily.Name)!;
        OdysseyFamily.ObservedLanding(restored, "engine", 0, 25,
            playerShip: true, successfullyLanded: true, routeDistanceInTiles: null,
            landedOnNewMap: null, arrivedMapId: "Map_103");
        OdysseyFamily.ObservedTakeoff(restored, "engine", 200, playerShip: true);
        OdysseyFamily.ObservedLanding(restored, "engine", 0, 25,
            playerShip: true, successfullyLanded: true, routeDistanceInTiles: null,
            landedOnNewMap: null, arrivedMapId: "Map_103");
        var final = resumed.PrepareSave(250, DateTime.UtcNow, "landed")!;
        var observation = CheckpointMetrics.ToObservation(final.Payload!, final.Json,
            new ColonyIdentity { ModCampaignId = "landed" }, null, null);
        Assert.Equal(2, observation.Scalar(OdysseyFamily.Flights,
            MetricKeys.Revision(OdysseyFamily.Flights))!.IntegerValue);
        Assert.Equal(1, observation.Scalar(OdysseyFamily.NewMapLandings,
            MetricKeys.Revision(OdysseyFamily.NewMapLandings))!.IntegerValue);
        Assert.Equal(MetricCoverage.Complete, observation.Scalar(OdysseyFamily.NewMapLandings,
            MetricKeys.Revision(OdysseyFamily.NewMapLandings))!.Coverage);
    }

    [Fact]
    public void NewConfirmedTakeoffReplacesAbandonedFlightAndItsStaleGenerationProof()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { OdysseyFamily.Family.Spec }, 0);
        var s = a.Family(OdysseyFamily.Name)!;
        OdysseyFamily.ObservedTakeoff(s, "engine", 100, playerShip: true);
        OdysseyFamily.ObservedMapGenerated(s, "engine", "Map_999", 110, confirmed: true);
        // First flight did not land. A later completed takeoff is a distinct
        // flight, not a continuation of the old pending interval.
        OdysseyFamily.ObservedTakeoff(s, "engine", 200, playerShip: true);
        OdysseyFamily.ObservedLanding(s, "engine", 0, 9,
            playerShip: true, successfullyLanded: true, routeDistanceInTiles: 7,
            landedOnNewMap: null, arrivedMapId: "Map_999");
        var saved = a.PrepareSave(230, DateTime.UtcNow, "retry")!;
        var row = CheckpointMetrics.ToObservation(saved.Payload!, saved.Json,
            new ColonyIdentity { ModCampaignId = "retry" }, null, null);
        var value = row.Scalar(OdysseyFamily.NewMapLandings,
            MetricKeys.Revision(OdysseyFamily.NewMapLandings))!;
        Assert.Equal(0, value.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);
        Assert.Equal(1, row.Scalar(OdysseyFamily.Flights,
            MetricKeys.Revision(OdysseyFamily.Flights))!.IntegerValue);
        Assert.Equal(7, row.Scalar(OdysseyFamily.Distance,
            MetricKeys.Revision(OdysseyFamily.Distance))!.RealValue);
    }

}
