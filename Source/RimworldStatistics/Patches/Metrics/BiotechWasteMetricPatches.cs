using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Waste production is counted only when the real CompWasteProducer.Waste
/// Thing stack increases after ProduceWaste, not on progress or a requested
/// production amount. Output already present before recording is excluded.
/// </summary>
[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(CompWasteProducer), nameof(CompWasteProducer.ProduceWaste))]
public static class BiotechProducedWasteMetricPatch
{
    public static void Prefix(CompWasteProducer __instance, out int __state)
        => __state = LiveMetrics.Family(BiotechFamily.Name) is not null
            && __instance.parent?.Faction == Faction.OfPlayer
                ? __instance.Waste?.stackCount ?? 0 : -1;

    public static void Postfix(CompWasteProducer __instance, int __state)
    {
        if (__state < 0 || __instance.parent?.Faction != Faction.OfPlayer)
            return;
        var produced = (__instance.Waste?.stackCount ?? 0) - __state;
        GameFacts.Record(BiotechFamily.Name, s =>
            BiotechFamily.WastePackProduced(s, produced, playerProductionComplete: produced > 0));
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
        => __state = LiveMetrics.Family(BiotechFamily.Name) is not null
            && __instance.parent?.Faction == Faction.OfPlayer ? __instance.SpaceLeft : -1;

    public static void Postfix(CompAtomizer __instance, int __state)
    {
        if (__state < 0 || __instance.parent?.Faction != Faction.OfPlayer)
            return;
        var destroyed = __instance.SpaceLeft - __state;
        GameFacts.Record(BiotechFamily.Name, s =>
            BiotechFamily.WastePackNeutralized(s, destroyed, playerNeutralizationComplete: destroyed > 0));
    }
}
