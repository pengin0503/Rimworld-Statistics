namespace RimworldStatistics.Core;

/// <summary>
/// Odyssey D2 completed-flight accounting. Production adapter is intentionally unavailable until a
/// confirmed landing with a stable flight id and authoritative route length can be observed.
/// </summary>
public static class OdysseyFamily
{
    public const string Name = "odyssey";
    public const string Flights = "odyssey.gravship.flights";
    public const string Distance = "odyssey.gravship.distance";
    public const string UniqueTiles = "odyssey.gravship.unique_tiles";
    public const string NewMapLandings = "odyssey.gravship.new_map_landings";
    private const string FlightIds = "completed_ids";
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
    }
}
