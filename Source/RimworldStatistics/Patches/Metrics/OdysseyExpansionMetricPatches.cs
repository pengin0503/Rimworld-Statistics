using System.Globalization;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

internal static class OdysseyVoyageProbe
{
    public static bool CanObserve()
        => UnityData.IsInMainThread
            && Scribe.mode == LoadSaveMode.Inactive
            && LiveMetrics.IsRecordingUnit(OdysseyVoyageFamily.Name)
            && ModsConfig.OdysseyActive;

    public static bool IsOrbitTile(PlanetTile tile)
        => tile.Valid && PlanetLayerDefOf.Orbit is not null && tile.LayerDef == PlanetLayerDefOf.Orbit;

    public static bool TryGetLandmarkFact(PlanetTile tile, out string normalizedTileKey, out string? qualifiedLandmarkDef)
    {
        normalizedTileKey = "";
        qualifiedLandmarkDef = null;
        if (!tile.Valid || Find.World?.landmarks?.landmarks is not { } map)
            return false;
        if (!map.TryGetValue(tile, out var landmark) || landmark is null)
            return false;
        normalizedTileKey = ColonyIdentity.NormalizeTile(tile.ToString()) ?? tile.ToString();
        var def = landmark.def;
        qualifiedLandmarkDef = def is null
            ? null
            : OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName);
        return true;
    }

    public static void ReconcileCurrentPlayerLocations(FamilyState s)
    {
        if (!ModsConfig.OdysseyActive || GameFacts.PlayerFaction is not { } player)
            return;
        if (Find.Maps is { } maps)
        {
            foreach (var map in maps)
            {
                if (map is null)
                    continue;
                var playerPresent = map.IsPlayerHome
                    || map.Parent?.Faction == player
                    || map.mapPawns?.FreeColonistsSpawnedCount > 0;
                if (playerPresent && TryGetLandmarkFact(map.Tile, out var tileKey, out var lmDef))
                    OdysseyVoyageFamily.LandmarkTileVisited(s, tileKey, lmDef, playerVisited: true);
            }
        }
        if (Find.WorldObjects?.Caravans is { } caravans)
        {
            foreach (var caravan in caravans)
            {
                if (caravan is not null && caravan.Faction == player
                    && TryGetLandmarkFact(caravan.Tile, out var tileKey, out var lmDef))
                    OdysseyVoyageFamily.LandmarkTileVisited(s, tileKey, lmDef, playerVisited: true);
            }
        }
    }
}

/// <summary>
/// Measures exact chemfuel deducted across active gravship fuel tanks during Building_GravEngine.ConsumeFuel.
/// </summary>
[MetricAdapter(OdysseyVoyageFamily.Name)]
[HarmonyPatch(typeof(Building_GravEngine), nameof(Building_GravEngine.ConsumeFuel))]
public static class OdysseyGravEngineFuelMetricPatch
{
    public static void Prefix(Building_GravEngine __instance, out (bool Tracked, float FuelBefore) __state)
    {
        __state = default;
        if (!OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (__instance is { Destroyed: false }
                && GameFacts.IsPlayerFaction(__instance.Faction)
                && LiveMetrics.CanCaptureEvent(OdysseyVoyageFamily.Name))
                __state = (true, __instance.TotalFuel);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }

    public static void Postfix(Building_GravEngine __instance, (bool Tracked, float FuelBefore) __state)
    {
        if (!__state.Tracked || !OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (__instance is null)
                return;
            var consumed = (double)__state.FuelBefore - __instance.TotalFuel;
            if (consumed > 1e-6)
                GameFacts.Record(OdysseyVoyageFamily.Name, s =>
                    OdysseyVoyageFamily.FuelConsumed(s, consumed, playerShip: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }
}

/// <summary>
/// Records Orbit layer arrivals and landmark visits when a player gravship completes landing.
/// </summary>
[MetricAdapter(OdysseyVoyageFamily.Name)]
[HarmonyPatch(typeof(WorldComponent_GravshipController), "LandingEnded")]
public static class OdysseyVoyageGravshipLandingMetricPatch
{
    public static void Prefix(Gravship ___gravship, PlanetTile ___landingTile, Map ___landingMap,
        out (Building_GravEngine? Engine, PlanetTile To, Map? Target) __state)
    {
        __state = default;
        if (!OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (LiveMetrics.CanCaptureEvent(OdysseyVoyageFamily.Name))
                __state = (___gravship?.Engine, ___landingTile, ___landingMap);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }

    public static void Postfix((Building_GravEngine? Engine, PlanetTile To, Map? Target) __state)
    {
        var (engine, to, targetMap) = __state;
        if (engine is null || !OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (engine.Destroyed || !engine.Spawned || !GameFacts.IsPlayerFaction(engine.Faction)
                || targetMap is null || engine.Map != targetMap || !to.Valid)
                return;
            var isOrbit = OdysseyVoyageProbe.IsOrbitTile(to);
            var hasLandmark = OdysseyVoyageProbe.TryGetLandmarkFact(to, out var tileKey, out var lmDef);
            if (!isOrbit && !hasLandmark)
                return;
            var arrivalId = "gravship:" + GameFacts.Id(engine) + ":" + GameFacts.Tick.ToString(CultureInfo.InvariantCulture)
                + ":" + (ColonyIdentity.NormalizeTile(to.ToString()) ?? to.ToString());
            GameFacts.Record(OdysseyVoyageFamily.Name, s =>
            {
                if (isOrbit)
                    OdysseyVoyageFamily.OrbitArrived(s, arrivalId, playerArrival: true, isOrbitLayer: true);
                if (hasLandmark)
                    OdysseyVoyageFamily.LandmarkTileVisited(s, tileKey, lmDef, playerVisited: true);
            });
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }
}

/// <summary>
/// Records Orbit layer arrivals and landmark visits when player transport pods or shuttles arrive at a destination tile.
/// </summary>
[MetricAdapter(OdysseyVoyageFamily.Name)]
[HarmonyPatch(typeof(TravellingTransporters), "Arrived")]
public static class OdysseyVoyageTransportersArrivedMetricPatch
{
    public static void Prefix(TravellingTransporters __instance, bool ___arrived,
        out (bool Tracked, PlanetTile Dest, string Id) __state)
    {
        __state = default;
        if (___arrived || !OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (__instance is not null
                && GameFacts.IsPlayerFaction(__instance.Faction)
                && __instance.destinationTile.Valid
                && LiveMetrics.CanCaptureEvent(OdysseyVoyageFamily.Name))
            {
                __state = (true, __instance.destinationTile, __instance.GetUniqueLoadID());
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }

    public static void Postfix(bool ___arrived, (bool Tracked, PlanetTile Dest, string Id) __state)
    {
        if (!__state.Tracked || !___arrived || !OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            var dest = __state.Dest;
            var isOrbit = OdysseyVoyageProbe.IsOrbitTile(dest);
            var hasLandmark = OdysseyVoyageProbe.TryGetLandmarkFact(dest, out var tileKey, out var lmDef);
            if (!isOrbit && !hasLandmark)
                return;
            var arrivalId = "transporter:" + __state.Id + ":" + GameFacts.Tick.ToString(CultureInfo.InvariantCulture);
            GameFacts.Record(OdysseyVoyageFamily.Name, s =>
            {
                if (isOrbit)
                    OdysseyVoyageFamily.OrbitArrived(s, arrivalId, playerArrival: true, isOrbitLayer: true);
                if (hasLandmark)
                    OdysseyVoyageFamily.LandmarkTileVisited(s, tileKey, lmDef, playerVisited: true);
            });
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }
}

/// <summary>
/// Records landmark visits when a player caravan steps onto a world tile carrying a Landmark.
/// </summary>
[MetricAdapter(OdysseyVoyageFamily.Name)]
[HarmonyPatch(typeof(Caravan_PathFollower), "TryEnterNextPathTile")]
public static class OdysseyVoyageCaravanStepMetricPatch
{
    public static void Prefix(Caravan ___caravan, out PlanetTile __state)
    {
        __state = PlanetTile.Invalid;
        if (!OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (___caravan is not null && GameFacts.IsPlayerFaction(___caravan.Faction))
                __state = ___caravan.Tile;
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }

    public static void Postfix(Caravan ___caravan, PlanetTile __state)
    {
        if (!__state.Valid || !OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (___caravan is null || !GameFacts.IsPlayerFaction(___caravan.Faction)
                || ___caravan.Tile == __state || !LiveMetrics.CanCaptureEvent(OdysseyVoyageFamily.Name))
                return;
            if (OdysseyVoyageProbe.TryGetLandmarkFact(___caravan.Tile, out var tileKey, out var lmDef))
                GameFacts.Record(OdysseyVoyageFamily.Name, s =>
                    OdysseyVoyageFamily.LandmarkTileVisited(s, tileKey, lmDef, playerVisited: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }
}

/// <summary>
/// Records landmark visits when a player caravan is created on an exit tile carrying a Landmark.
/// </summary>
[MetricAdapter(OdysseyVoyageFamily.Name)]
[HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
    new[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile), typeof(PlanetTile), typeof(PlanetTile), typeof(bool) })]
public static class OdysseyVoyageCaravanExitMetricPatch
{
    public static void Postfix(Caravan __result)
    {
        if (!OdysseyVoyageProbe.CanObserve())
            return;
        try
        {
            if (__result is null || !GameFacts.IsPlayerFaction(__result.Faction)
                || !LiveMetrics.CanCaptureEvent(OdysseyVoyageFamily.Name))
                return;
            if (OdysseyVoyageProbe.TryGetLandmarkFact(__result.Tile, out var tileKey, out var lmDef))
                GameFacts.Record(OdysseyVoyageFamily.Name, s =>
                    OdysseyVoyageFamily.LandmarkTileVisited(s, tileKey, lmDef, playerVisited: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(OdysseyVoyageFamily.Name, e);
        }
    }
}
