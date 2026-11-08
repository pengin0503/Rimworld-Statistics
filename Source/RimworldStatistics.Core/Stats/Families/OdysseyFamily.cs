namespace RimworldStatistics.Core;

/// <summary>
/// Odyssey D2 completed-flight accounting. The production adapter observes takeoff,
/// confirmed landing, map generation and the vanilla route solver; behavioral
/// validation in RimWorld is still pending behind the Unverified gate.
/// </summary>
public static class OdysseyFamily
{
    public const string Name = "odyssey";
    public const string Flights = "odyssey.gravship.flights";
    public const string Distance = "odyssey.gravship.distance";
    public const string UniqueTiles = "odyssey.gravship.unique_tiles";
    public const string NewMapLandings = "odyssey.gravship.new_map_landings";
    private const string FlightIds = "completed_ids";
    private const string PendingTakeoffs = "pending_takeoff";
    private const string KnownMaps = "known_maps_before_flight";
    private const string RecordedMapBaselines = "map_baselines";
    private const string GeneratedForFlight = "generated_for_flight";
    private const string Arrivals = "unique_arrival";
    private const string Count = "flights";
    private const string Travel = "distance";
    private const string NewMaps = "new_map";
    private const string MissingDistance = "distance_unknown";
    private const string MissingMapKind = "map_kind_unknown";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 1, RequiredDlc = AcquisitionUnits.Odyssey, Build = Build,
    };

    /// <summary>
    /// Capture already-loaded maps before initiating a flight. Presence proves
    /// a revisit, but absence does NOT prove new generation: a prior map might
    /// simply be unloaded. New generation needs a separate confirmed hook.
    /// </summary>
    public static void ObservedExistingMaps(FamilyState s, IEnumerable<string> existingMapIds)
    {
        var seen = false;
        foreach (var mapId in existingMapIds)
        {
            if (string.IsNullOrWhiteSpace(mapId))
                continue;
            s.Distinct(KnownMaps, mapId);
            seen = true;
        }
        if (seen)
            s.Count(RecordedMapBaselines);
    }

    /// <summary>
    /// Only an actual map generation completed for the pending player's flight proves a map is new.
    /// A map missing from the preflight loaded-map set may simply be unloaded or revisited.
    /// Generation markers survive checkpoint reloads and are consumed on a successful landing.
    /// </summary>
    public static void ObservedMapGenerated(FamilyState s, string engineId, string mapId, int tick, bool confirmed)
    {
        if (!confirmed || tick < 0 || string.IsNullOrWhiteSpace(engineId) || string.IsNullOrWhiteSpace(mapId))
            return;
        if (s.Tracked(PendingTakeoffs, engineId) is { } start && tick >= start)
            s.Track(GeneratedForFlight, engineId + "|" + mapId, tick);
    }

    private static void ForgetFlightMapGeneration(FamilyState s, string engineId)
    {
        if (s.Active.TryGetValue(GeneratedForFlight, out var mapIds))
            foreach (var id in mapIds.Keys.Where(id => id.StartsWith(engineId + "|", StringComparison.Ordinal)).ToArray())
                s.Untrack(GeneratedForFlight, id);
    }

    /// <summary>
    /// A new confirmed takeoff supersedes an older unmatched attempt for this
    /// engine (for example after an aborted flight). A repeated notification at
    /// the same tick is idempotent; older out-of-order notifications are ignored.
    /// </summary>
    public static void ObservedTakeoff(FamilyState s, string engineId, int tick, bool playerShip)
    {
        if (!playerShip || tick < 0 || string.IsNullOrWhiteSpace(engineId))
            return;
        if (s.Tracked(PendingTakeoffs, engineId) is { } previous && tick <= previous)
            return;
        ForgetFlightMapGeneration(s, engineId);
        s.Track(PendingTakeoffs, engineId, tick);
    }

    /// <summary>
    /// Match a confirmed landing to a persisted takeoff marker. Failed or aborted
    /// landing attempts keep the pending flight so a later retry can complete.
    /// </summary>
    public static void ObservedLanding(FamilyState s, string engineId, int arrivalLayer,
        long arrivalTile, bool playerShip, bool successfullyLanded, double? routeDistanceInTiles,
        bool? landedOnNewMap, string? arrivedMapId = null)
    {
        if (!playerShip || !successfullyLanded || arrivalLayer < 0 || arrivalTile < 0
            || string.IsNullOrWhiteSpace(engineId))
            return;
        var start = s.Untrack(PendingTakeoffs, engineId);
        if (start is not { } tick)
            return;
        // Newly generated requires affirmative proof from MapGenerator.GenerateMap.
        // The absence of a loaded map at takeoff alone is NOT that proof.
        if (landedOnNewMap is null && !string.IsNullOrWhiteSpace(arrivedMapId))
        {
            var generationTick = s.Tracked(GeneratedForFlight, engineId + "|" + arrivedMapId);
            if (generationTick is { } generatedAt && generatedAt >= tick)
                landedOnNewMap = true;
            else if (s.Peek(KnownMaps)?.By?.ContainsKey(arrivedMapId) == true)
                landedOnNewMap = false;
        }
        ForgetFlightMapGeneration(s, engineId);
        FlightCompleted(s, engineId + "/" + tick.ToString(System.Globalization.CultureInfo.InvariantCulture),
            arrivalLayer, arrivalTile, playerShip, successfullyLanded, routeDistanceInTiles, landedOnNewMap);
        if (!string.IsNullOrWhiteSpace(arrivedMapId))
            s.Distinct(KnownMaps, arrivedMapId);
    }

    /// <summary>
    /// One authoritative completed landing, never a preview, launch, cancelled/failed flight or map load.
    /// RouteDistanceInTiles must be the completed route's tile length, not preview fuel cost.
    /// </summary>
    public static void FlightCompleted(FamilyState s, string flightId, int arrivalLayer, long arrivalTile,
        bool playerShip, bool successfullyLanded, double? routeDistanceInTiles, bool? landedOnNewMap)
    {
        if (!playerShip || !successfullyLanded || string.IsNullOrWhiteSpace(flightId)
            || arrivalLayer < 0 || arrivalTile < 0)
            return;
        if (!s.Distinct(FlightIds, flightId))
            return;
        s.Count(Count);
        s.Distinct(Arrivals, arrivalLayer.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + arrivalTile.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (routeDistanceInTiles is { } km && double.IsFinite(km) && km >= 0)
            s.Observe(Travel, km);
        else
            s.Count(MissingDistance);
        if (landedOnNewMap is true)
            s.Count(NewMaps);
        else if (!landedOnNewMap.HasValue)
            s.Count(MissingMapKind);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Count, Flights);
        c.PutSum(Travel, Distance);
        c.PutDistinct(Arrivals, UniqueTiles);
        c.PutCount(NewMaps, NewMapLandings);
        if (c.State.Peek(MissingDistance)?.N > 0)
            c.MarkPartial(Distance);
        if (c.State.Peek(MissingMapKind)?.N > 0)
            c.MarkPartial(NewMapLandings);
        if (c.State.Peek(Arrivals)?.Overflow == true)
            c.MarkPartial(UniqueTiles);
        if (c.State.Peek(FlightIds)?.Overflow == true)
            c.MarkPartial(Flights, Distance, UniqueTiles, NewMapLandings);
        if (c.State.TrackerOverflowed(PendingTakeoffs))
            c.MarkPartial(Flights, Distance, UniqueTiles, NewMapLandings);
        if (c.State.Peek(KnownMaps)?.Overflow == true || c.State.TrackerOverflowed(GeneratedForFlight))
            c.MarkPartial(NewMapLandings);
    }
}
