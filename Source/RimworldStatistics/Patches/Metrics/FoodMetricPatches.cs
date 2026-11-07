using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>Eating (evidence #food): Thing.Ingested returns the nutrition actually taken.</summary>
[MetricAdapter(FoodFamily.Name)]
[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
public static class IngestedMetricPatch
{
    public static void Postfix(float __result, Pawn ingester)
    {
        if (ingester == null || !(__result > 0f))
            return;
        var free = ingester.IsFreeColonist;
        var n = (double)__result;
        GameFacts.Record(FoodFamily.Name, s => FoodFamily.Ate(s, n, free));
    }
}

/// <summary>
/// Spoilage (evidence #food): CompRottable's private TickInterval destroys a stack whose rot completed. The prefix notes a
/// player food stack that is still there; the postfix confirms it was destroyed by that tick. Runs per rottable tick, so the
/// prefix is a few field reads.
/// </summary>
[MetricAdapter(FoodFamily.Name)]
[HarmonyPatch(typeof(CompRottable), "TickInterval")]
public static class RotMetricPatch
{
    public static void Prefix(CompRottable __instance, out int __state)
    {
        __state = 0;
        var t = __instance.parent;
        if (t == null || t.Destroyed || !t.def.IsNutritionGivingIngestible || t is Corpse || t.MapHeld is not { IsPlayerHome: true })
            return;
        var holder = t.ParentHolder;
        if (holder is Map || (holder is ThingOwner { Owner: Thing owner } && GameFacts.IsPlayer(owner)))
            __state = t.stackCount;
    }

    public static void Postfix(CompRottable __instance, int __state)
    {
        if (__state > 0 && __instance.parent.Destroyed && __instance.Stage == RotStage.Rotting)
            GameFacts.Record(FoodFamily.Name, s => FoodFamily.Spoiled(s, __state));
    }
}
