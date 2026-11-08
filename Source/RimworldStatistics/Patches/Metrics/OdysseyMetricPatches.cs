using System.Globalization;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>Stable (tile,layer) parsing from PlanetTile's authoritative serialized representation.</summary>
internal static class OdysseyArrivalFacts
{
    public static bool Parse(PlanetTile tile, out long tileId, out int layerId)
    {
        tileId = -1; layerId = -1;
        if (!tile.Valid)
            return false;
        var parts = tile.ToString().Split(',');
        return parts.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out tileId)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out layerId)
            && tileId >= 0 && layerId >= 0;
    }

    /// <summary>
    /// Ask the vanilla flight path solver for its actual traversal distance, but
    /// only after the original engine has completed landing. This is not a
    /// straight-line estimate: the solver uses WorldGrid.TraversalDistanceBetween
    /// and PlanetLayer.TryGetPath for multi-layer routes (1.7.4985 rev1269).
    /// A missing or invalid path remains unknown rather than becoming zero.
    /// </summary>
    public static double? ConfirmedRouteDistance(PlanetTile from, PlanetTile to)
    {
        if (!from.Valid || !to.Valid)
            return null;
        try
        {
            return GravshipUtility.TryGetPathFuelCost(from, to, out _, out var distance, 1f, 1f)
                && distance >= 0 ? distance : null;
        }
        catch (Exception e)
        {
            Log.WarningOnce("[RimworldStatistics] Unable to resolve a completed gravship path; "
                + "flight distance remains unknown. " + e, "RWS.odyssey.path".GetHashCode());
            return null;
        }
    }
}

/// <summary>
/// A completed takeoff cutscene, not a preview or launch request, starts a persistent
/// flight interval. The pending interval can survive an in-flight save/load.
/// </summary>
/// <summary>
/// Record the identities of all existing maps before launch. Taking this
/// snapshot never counts as a successful takeoff, and remains cumulative in
/// the saved Core family. It distinguishes generated maps from revisits.
/// </summary>
[MetricAdapter(OdysseyFamily.Name)]
[HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateTakeoff))]
public static class OdysseyPreflightMapsMetricPatch
{
    public static void Prefix(Building_GravEngine engine)
    {
        if (engine is null || engine.Destroyed || engine.Faction != Faction.OfPlayer
            || LiveMetrics.Family(OdysseyFamily.Name) is null)
            return;
        var maps = Find.Maps?.Where(m => m is not null)
            .Select(m => GameFacts.MapId(m)).ToArray() ?? Array.Empty<string>();
        GameFacts.Record(OdysseyFamily.Name, s => OdysseyFamily.ObservedExistingMaps(s, maps));
    }
}

[MetricAdapter(OdysseyFamily.Name)]
[HarmonyPatch(typeof(WorldComponent_GravshipController), "TakeoffEnded")]
public static class OdysseyTakeoffMetricPatch
{
    public static void Prefix(Gravship ___gravship, out Building_GravEngine? __state)
        => __state = LiveMetrics.Family(OdysseyFamily.Name) is not null ? ___gravship?.Engine : null;

    public static void Postfix(Building_GravEngine? __state)
    {
        var engine = __state;
        if (engine is null || engine.Destroyed || engine.Faction != Faction.OfPlayer)
            return;
        GameFacts.Record(OdysseyFamily.Name, s => OdysseyFamily.ObservedTakeoff(
            s, GameFacts.Id(engine), GameFacts.Tick, playerShip: true));
    }
}

/// <summary>
/// A confirmed map-generation result is positive evidence that the destination
/// map was created during the pending flight. Merely missing from Find.Maps at
/// takeoff is not proof: an old map may have been unloaded.
/// </summary>
[MetricAdapter(OdysseyFamily.Name)]
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
public static class OdysseyNewLandingMapMetricPatch
{
    public static void Postfix(Map __result)
    {
        if (__result is null || LiveMetrics.Family(OdysseyFamily.Name) is null)
            return;
        var ship = Find.CurrentGravship;
        var engine = ship?.Engine;
        if (ship is null || engine is null || engine.Destroyed || engine.Faction != Faction.OfPlayer
            || !ship.destinationTile.Valid || !__result.Tile.Equals(ship.destinationTile))
            return;
        GameFacts.Record(OdysseyFamily.Name, s => OdysseyFamily.ObservedMapGenerated(
            s, GameFacts.Id(engine), GameFacts.MapId(__result), GameFacts.Tick, confirmed: true));
    }
}

/// <summary>
/// Only count landings that actually place the original player engine in the
/// landing map. Aborted landing attempts cannot pass this post-condition.
/// </summary>
[MetricAdapter(OdysseyFamily.Name)]
[HarmonyPatch(typeof(WorldComponent_GravshipController), "LandingEnded")]
public static class OdysseyLandingMetricPatch
{
    public static void Prefix(Gravship ___gravship, PlanetTile ___takeoffTile,
        PlanetTile ___landingTile, Map ___landingMap,
        out (Building_GravEngine? Engine, PlanetTile From, PlanetTile To, Map? Target) __state)
        => __state = LiveMetrics.Family(OdysseyFamily.Name) is not null
            ? (___gravship?.Engine, ___takeoffTile, ___landingTile, ___landingMap)
            : default;

    public static void Postfix((Building_GravEngine? Engine, PlanetTile From, PlanetTile To, Map? Target) __state)
    {
        var (engine, from, to, targetMap) = __state;
        if (engine is null || engine.Destroyed || !engine.Spawned
            || engine.Faction != Faction.OfPlayer || engine.Map != targetMap
            || targetMap is null || !OdysseyArrivalFacts.Parse(to, out var tileId, out var layerId))
            return;
        // The landing has completed and the original player engine is spawned
        // at the destination. Resolve the vanilla traversal route now, not from
        // a launch UI preview or a geometric approximation.
        var length = OdysseyArrivalFacts.ConfirmedRouteDistance(from, to);
        GameFacts.Record(OdysseyFamily.Name, s => OdysseyFamily.ObservedLanding(
            s, GameFacts.Id(engine), layerId, tileId,
            playerShip: true, successfullyLanded: true,
            routeDistanceInTiles: length, landedOnNewMap: null,
            arrivedMapId: GameFacts.MapId(targetMap)));
    }
}
