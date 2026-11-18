namespace RimworldStatistics.Core;

/// <summary>
/// Odyssey DLC expansion live events (§4.1.4): cumulative launch chemfuel consumed,
/// Orbit layer arrivals, and distinct visited world landmark tiles by LandmarkDef.
/// Kept in its own acquisition unit so pre-upgrade Odyssey checkpoints retain their
/// existing flight history uninterrupted while new metrics start cleanly at their load tick.
/// </summary>
public static class OdysseyVoyageFamily
{
    public const string Name = "odyssey_voyage";
    public const string ChemfuelConsumed = "odyssey.gravship.chemfuel_consumed";
    public const string OrbitArrivals = "odyssey.gravship.orbit_arrivals";
    public const string VisitedLandmarks = "odyssey.landmarks.visited";

    private const string FuelAcc = "chemfuel_consumed";
    private const string OrbitIds = "orbit_arrival_ids";
    private const string OrbitCount = "orbit_arrivals";
    private const string VisitedLandmarkTiles = "visited_landmark_tiles";
    private const string LandmarkCounts = "landmark_counts";
    private const string LandmarkUnknown = "landmark_unknown";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Odyssey,
        Build = Build,
    };

    /// <summary>Records positive chemfuel actually deducted from player gravship tanks during launch.</summary>
    public static void FuelConsumed(FamilyState s, double consumedChemfuel, bool playerShip)
    {
        if (!playerShip || !double.IsFinite(consumedChemfuel) || consumedChemfuel <= 1e-6)
            return;
        s.Observe(FuelAcc, consumedChemfuel);
    }

    /// <summary>Records one confirmed player arrival in the Orbit planet layer.</summary>
    public static void OrbitArrived(FamilyState s, string arrivalEventId, bool playerArrival, bool isOrbitLayer)
    {
        if (!playerArrival || !isOrbitLayer || string.IsNullOrWhiteSpace(arrivalEventId))
            return;
        if (s.Distinct(OrbitIds, arrivalEventId))
            s.Count(OrbitCount);
    }

    /// <summary>
    /// Records the first time the player visits a world tile carrying a landmark,
    /// grouped by package-qualified <c>LandmarkDef</c>. Revisiting the same tile does not double-count.
    /// </summary>
    public static void LandmarkTileVisited(
        FamilyState s,
        string normalizedTileKey,
        string? qualifiedLandmarkDef,
        bool playerVisited)
    {
        if (!playerVisited || string.IsNullOrWhiteSpace(normalizedTileKey))
            return;
        if (!s.Distinct(VisitedLandmarkTiles, normalizedTileKey))
            return;
        var bucket = string.IsNullOrWhiteSpace(qualifiedLandmarkDef) ? MetricKeys.UnknownBucket : qualifiedLandmarkDef!;
        s.Count(LandmarkCounts);
        s.CountBy(LandmarkCounts, bucket);
        if (bucket == MetricKeys.UnknownBucket)
            s.Count(LandmarkUnknown);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutSum(FuelAcc, ChemfuelConsumed);
        c.PutCount(OrbitCount, OrbitArrivals);
        c.CountAndBreakdown(LandmarkCounts, VisitedLandmarks, VisitedLandmarks);

        var s = c.State;
        if (s.Peek(OrbitIds)?.Overflow == true)
            c.MarkPartial(OrbitArrivals);
        if (s.Peek(VisitedLandmarkTiles)?.Overflow == true || (s.Peek(LandmarkUnknown)?.N ?? 0) > 0)
            c.MarkPartial(VisitedLandmarks);
    }
}
