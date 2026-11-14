using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class RoadOutcomes
{
    private const string Owner = "Mlie.RoadsOfTheRim";
    private const string Unit = "mod.mlie.roadsoftherim.roadsegments";
    private static FieldInfo road = null!;
    private static MethodInfo next = null!;
    private static Type siteType = null!;
    private sealed record Segment(PlanetTile From, PlanetTile To, RoadDef Road);

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count(Owner, "roadsegments", "RWS_NewMod_Roads", MetricCategory.Exploration, Install, breakdown: true);

    private static void Install(Harmony harmony)
    {
        var comp = OutcomeMetric.Bind("RoadsOfTheRim.WorldObjectComp_ConstructionSite", "RoadsOfTheRim");
        siteType = OutcomeMetric.Bind("RoadsOfTheRim.RoadConstructionSite", "RoadsOfTheRim");
        var leg = OutcomeMetric.Bind("RoadsOfTheRim.RoadConstructionLeg", "RoadsOfTheRim");
        road = AccessTools.DeclaredField(siteType, "roadDef");
        next = OutcomeMetric.Method(siteType, "GetNextLeg", leg);
        if (!typeof(WorldObjectComp).IsAssignableFrom(comp) || !typeof(WorldObject).IsAssignableFrom(leg) ||
            road?.FieldType != typeof(RoadDef))
            throw new InvalidOperationException("Road construction signature changed");
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(comp, "FinishWork", typeof(bool), typeof(Caravan)),
            typeof(RoadOutcomes));
    }

    private static void Before(WorldObjectComp __instance, Caravan? __0, out Segment? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) || __0 is not null && __0.Faction != Faction.OfPlayer)
            return;
        try
        {
            var site = __instance.parent;
            // The player-only construction-site command does not assign a faction to the site.
            // A null caravan is used for help from an allied faction on that site.
            if (!siteType.IsInstanceOfType(site) || site.Faction is { IsPlayer: false } ||
                road.GetValue(site) is not RoadDef def || next.Invoke(site, null) is not WorldObject leg ||
                !site.Tile.Valid || !leg.Tile.Valid || site.Tile == leg.Tile ||
                Find.WorldGrid[site.Tile] is not SurfaceTile from || Find.WorldGrid[leg.Tile] is not SurfaceTile to)
                return;

            bool AlreadyAsGood(SurfaceTile tile, PlanetTile other)
                => tile.potentialRoads?.Any(link => link.neighbor == other && link.road is { } existing &&
                    existing.movementCostMultiplier <= def.movementCostMultiplier) == true;
            if (!AlreadyAsGood(from, leg.Tile) && !AlreadyAsGood(to, site.Tile))
                __state = new Segment(site.Tile, leg.Tile, def);
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }

    private static void After(bool __result, bool __runOriginal, Segment? __state)
    {
        if (!__runOriginal || !__result || __state is not { } segment)
            return;
        try
        {
            bool Linked(PlanetTile tile, PlanetTile other) => Find.WorldGrid[tile] is SurfaceTile surface &&
                surface.potentialRoads?.Any(link => link.neighbor == other && link.road == segment.Road) == true;
            if (NewModOutcomeRules.RoadImproved(Linked(segment.From, segment.To), Linked(segment.To, segment.From)))
                OutcomeMetric.Record(Unit, segment.Road, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }
}
