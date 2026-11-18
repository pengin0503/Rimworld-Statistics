using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Waste production is counted only when the wastepacks the producer actually holds increase across ProduceWaste, not on
/// progress or a requested production amount. Output already present before recording is excluded. All wastepack stacks
/// of its container count (#323): ProduceWaste adds stacks of at most the stack limit, and CompWasteProducer.Waste is only
/// the first of them, so a full first stack hid every later pack.
/// </summary>
[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(CompWasteProducer), nameof(CompWasteProducer.ProduceWaste))]
public static class BiotechProducedWasteMetricPatch
{
    public static void Prefix(CompWasteProducer __instance, out int __state)
    {
        __state = -1;
        try
        {
            if (__instance?.parent is null || !GameFacts.IsPlayerFaction(__instance.parent.Faction)
                || !LiveMetrics.CanCaptureEvent(BiotechFamily.Name))
                return;
            __state = Held(__instance);
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }

    public static void Postfix(CompWasteProducer __instance, int __state)
    {
        if (__state < 0)
            return;
        try
        {
            if (__instance?.parent is null || !GameFacts.IsPlayerFaction(__instance.parent.Faction))
                return;
            var produced = Held(__instance) - __state;
            GameFacts.Record(BiotechFamily.Name, s =>
                BiotechFamily.WastePackProduced(s, produced, playerProductionComplete: produced > 0));
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }

    /// <summary>The container ProduceWaste adds to (the same owner as CompWasteProducer.Waste and its inspect string).</summary>
    private static int Held(CompWasteProducer producer)
    {
        if (producer.parent?.TryGetInnerInteractableThingOwner() is not { } owner)
            return 0;
        var held = new List<(bool, int)>(owner.Count);
        for (var i = 0; i < owner.Count; i++)
            if (owner[i] is { } thing)
                held.Add((thing.def == ThingDefOf.Wastepack, thing.stackCount));
        return BiotechFamily.Wastepacks(held);
    }
}

/// <summary>
/// Only actual post-atomization freed capacity is an attributable player
/// neutralization. Merely hauling waste into storage is not disposal.
/// </summary>
[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(CompAtomizer), "DoAtomize")]
public static class BiotechAtomizedWasteMetricPatch
{
    public static void Prefix(CompAtomizer __instance, out int __state)
    {
        __state = -1;
        try
        {
            if (__instance?.parent is null || !GameFacts.IsPlayerFaction(__instance.parent.Faction)
                || !LiveMetrics.CanCaptureEvent(BiotechFamily.Name))
                return;
            __state = __instance.SpaceLeft;
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }

    public static void Postfix(CompAtomizer __instance, int __state)
    {
        if (__state < 0)
            return;
        try
        {
            if (__instance?.parent is null || !GameFacts.IsPlayerFaction(__instance.parent.Faction))
                return;
            var destroyed = __instance.SpaceLeft - __state;
            GameFacts.Record(BiotechFamily.Name, s =>
                BiotechFamily.WastePackNeutralized(s, destroyed, playerNeutralizationComplete: destroyed > 0));
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }
}
