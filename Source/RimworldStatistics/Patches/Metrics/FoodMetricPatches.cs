using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>Eating (evidence #food): Thing.Ingested returns the nutrition actually taken.</summary>
[MetricAdapter(FoodFamily.Name, ChoreFamily.Name)]
[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
public static class IngestedMetricPatch
{
    public static void Postfix(Thing __instance, float __result, Pawn ingester)
    {
        if (ingester == null || !(__result > 0f))
            return;
        var free = ingester.IsFreeColonist;
        var n = (double)__result;
        GameFacts.Record(FoodFamily.Name, s => FoodFamily.Ate(s, n, free));

        // Reuse the original low-frequency successful-ingestion hook.
        // A meal eaten by a player-faction humanlike is distinct from the
        // already-captured nutrition amount. No extra Harmony patch needed.
        if (__instance.def?.ingestible?.IsMeal == true &&
            ingester.RaceProps.Humanlike && GameFacts.IsPlayer(ingester))
            GameFacts.Record(ChoreFamily.Name, ChoreFamily.MealIngested);
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

/// <summary>
/// N2-F-010: the colony's food stock as the game counts it. <c>ResourceCounter.UpdateResourceCounts</c> (1.7.4985) recounts the
/// stored, fresh, unfogged resources of one map every 204 ticks and on hauling; the stock has food when that count holds a
/// human-edible nutrition-giving def, or a player pawn on the map carries such food (a stack between two stockpiles). The colony
/// has food when any player home map has; a home map not recounted since the load makes the state unknown until it is.
/// </summary>
internal static class FoodStockProbe
{
    private static readonly Dictionary<int, bool> ByMap = new();

    public static void Reset() => ByMap.Clear();

    public static void Forget(Map map) => ByMap.Remove(map.uniqueID);

    /// <summary>
    /// The defs <c>ResourceCounter.TotalHumanEdibleNutrition</c> sums (IsNutritionGivingIngestible and HumanEdible, weighted by
    /// the nutrition stat): "> 0" here is the same test with an early exit and the cached stat, because this runs on every
    /// recount (every haul) while the vanilla property recomputes the stat of every counted def on each call.
    /// </summary>
    private static bool Edible(ThingDef def)
        => def.IsNutritionGivingIngestible && def.ingestible.HumanEdible && def.ingestible.CachedNutrition > 0f;

    private static bool HasFood(Map map)
    {
        foreach (var (def, count) in map.resourceCounter.AllCountedAmounts)
            if (count > 0 && def != null && Edible(def))
                return true;
        foreach (var pawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            if (pawn.carryTracker?.CarriedThing?.GetInnerIfMinified() is { } carried && carried.def.CountAsResource && Edible(carried.def)
                && !carried.IsNotFresh())
                return true;
        return false;
    }

    /// <summary>After <paramref name="recounted"/>'s recount: the colony has food, has none, or is not known yet (null).</summary>
    public static bool? Colony(Map recounted)
    {
        ByMap[recounted.uniqueID] = HasFood(recounted);
        var unknown = false;
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome)
                continue;
            if (!ByMap.TryGetValue(map.uniqueID, out var food))
                unknown = true;
            else if (food)
                return true;
        }
        return unknown ? null : false;
    }
}

[MetricAdapter(FoodFamily.Name)]
[HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.UpdateResourceCounts))]
public static class FoodStockMetricPatch
{
    public static void Postfix(Map ___map)
    {
        if (___map is not { IsPlayerHome: true } || LiveMetrics.Family(FoodFamily.Name) is null)
            return;
        try
        {
            if (FoodStockProbe.Colony(___map) is not { } hasFood)
                return;
            var tick = GameFacts.Tick;
            GameFacts.Record(FoodFamily.Name, s => FoodFamily.StockObserved(s, hasFood, tick));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(FoodFamily.Name, e);
        }
    }
}

/// <summary>
/// N1-A-008: the cells of every growing zone (only the player's designators and gravship landings create <c>Zone_Growing</c> in
/// 1.7.4985). Cells change through <c>Zone.AddCell</c> (which <c>Zone_Growing.AddCell</c> calls) and <c>Zone.RemoveCell</c>
/// (also used by <c>Zone.Delete</c>, gravship takeoff and buildings placed over a zone); a removed map takes its zones along.
/// </summary>
internal static class GrowingProbe
{
    public static int CellsOn(Map map)
    {
        var n = 0;
        foreach (var zone in map.zoneManager.AllZones)
            if (zone is Zone_Growing growing)
                n += growing.CellCount;
        return n;
    }

    public static int Cells() => Find.Maps.Sum(CellsOn);

    public static void Changed(int delta)
    {
        if (delta == 0)
            return;
        var tick = GameFacts.Tick;
        GameFacts.Record(FoodFamily.Name, s => FoodFamily.GrowingCellsChanged(s, delta, tick));
    }
}

[MetricAdapter(FoodFamily.Name)]
[HarmonyPatch(typeof(Zone), nameof(Zone.AddCell))]
public static class GrowingCellAddedMetricPatch
{
    public static void Prefix(Zone __instance, out int __state) => __state = __instance is Zone_Growing ? __instance.CellCount : -1;

    public static void Postfix(Zone __instance, int __state)
    {
        if (__state >= 0)
            GrowingProbe.Changed(__instance.CellCount - __state);
    }
}

[MetricAdapter(FoodFamily.Name)]
[HarmonyPatch(typeof(Zone), nameof(Zone.RemoveCell))]
public static class GrowingCellRemovedMetricPatch
{
    public static void Prefix(Zone __instance, out int __state) => __state = __instance is Zone_Growing ? __instance.CellCount : -1;

    public static void Postfix(Zone __instance, int __state)
    {
        if (__state >= 0)
            GrowingProbe.Changed(__instance.CellCount - __state);
    }
}

/// <summary>N1-A-008's numerator: the main harvest (QuestManager.Notify_PlantHarvested) of a player's crop standing in a growing zone.</summary>
[MetricAdapter(FoodFamily.Name)]
[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Notify_PlantHarvested))]
public static class ZoneHarvestMetricPatch
{
    public static void Postfix(Pawn worker, Thing harvested)
    {
        if (LiveMetrics.Family(FoodFamily.Name) is null)
            return;
        try
        {
            if (HarvestProbe.PlayerCrop(worker, harvested) is not { } plant || plant.Map.zoneManager.ZoneAt(plant.Position) is not Zone_Growing)
                return;
            var units = harvested.stackCount;
            var tick = GameFacts.Tick;
            GameFacts.Record(FoodFamily.Name, s => FoodFamily.ZoneHarvested(s, units, tick));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(FoodFamily.Name, e);
        }
    }
}

[MetricAdapter(FoodFamily.Name)]
[HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
public static class FoodMapRemovedMetricPatch
{
    public static void Prefix(Map map)
    {
        if (map == null || LiveMetrics.Family(FoodFamily.Name) is null)
            return;
        try   // a failure here must close the food family only, never break the map's removal
        {
            if (!Find.Maps.Contains(map))
                return;
            FoodStockProbe.Forget(map);
            GrowingProbe.Changed(-GrowingProbe.CellsOn(map));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(FoodFamily.Name, e);
        }
    }
}
