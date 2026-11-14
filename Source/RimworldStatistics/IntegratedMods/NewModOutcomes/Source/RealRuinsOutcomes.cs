using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class RealRuinsOutcomes
{
    private const string Unit = "mod.woolstrand.realruins.poiarrivals";
    private static FieldInfo destination = null!;
    private sealed record Arrival(MapParent Site, Pawn[] Travellers);

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count("Woolstrand.RealRuins", "poiarrivals", "RWS_NewMod_RealRuins", "RWS_NewMod_RealRuinsDesc",
            MetricCategory.Exploration, Install);

    private static void Install(Harmony harmony)
    {
        var action = OutcomeMetric.Bind("RealRuins.CaravanArrivalAction_VisitRealRuinsPOI", "RealRuins");
        destination = AccessTools.Field(action, "mapParent");
        if (destination?.FieldType != typeof(MapParent))
            throw new InvalidOperationException("Real Ruins caravan destination changed");
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(action, "DoEnter", typeof(void), typeof(Caravan)),
            typeof(RealRuinsOutcomes));
    }

    private static void Before(object __instance, Caravan __0, out Arrival? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) || __0.Faction != Faction.OfPlayer)
            return;
        try
        {
            if (destination.GetValue(__instance) is MapParent { Spawned: true, HasMap: false } site &&
                site.Tile == __0.Tile && __0.PawnsListForReading.Count > 0)
                __state = new Arrival(site, __0.PawnsListForReading.ToArray());
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }

    private static void After(bool __runOriginal, Arrival? __state)
    {
        if (!__runOriginal || __state is not { Site.Map: { } map } arrival)
            return;
        try
        {
            if (arrival.Travellers.Any(pawn => pawn.Spawned && pawn.Map == map))
                OutcomeMetric.Record(Unit);
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }
}
