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

    /// <summary>Direct vanilla world-grid distance; no speculative distance for cross-layer or unavailable maps.</summary>
    public static double? ConfirmedDirectDistance(PlanetTile from, PlanetTile to)
    {
        var grid = Find.World?.grid;
        if (grid is null || !grid.InBounds(from) || !grid.InBounds(to))
            return null;
        if (!Parse(from, out _, out var fromLayer) || !Parse(to, out _, out var toLayer) || fromLayer != toLayer)
            return null;
        var distance = grid.ApproxDistanceInTiles(from, to);
        return float.IsFinite(distance) && distance >= 0 ? distance : null;
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
        // ApproxDistanceInTiles is a geometric estimate, NOT a confirmed
        // travelled path length. A conservative unknown prevents a false
        // number from being presented as an authoritative flight distance.
        double? length = null;
        GameFacts.Record(OdysseyFamily.Name, s => OdysseyFamily.ObservedLanding(
            s, GameFacts.Id(engine), layerId, tileId,
            playerShip: true, successfullyLanded: true,
            routeDistanceInTiles: length, landedOnNewMap: null,
            arrivedMapId: GameFacts.MapId(targetMap)));
    }
}
