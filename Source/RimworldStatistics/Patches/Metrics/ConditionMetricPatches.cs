using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Disasters (evidence #disaster): game conditions of the player's homes or the world from RegisterCondition to End; incidents
/// by IncidentWorker.TryExecute success (not the true it returns for a map without colonists, #352); fires started outside Fire.TrySpread on a player home map in a cell without fire; and
/// completed player buildings killed by flame damage.
/// </summary>
internal static class ConditionProbe
{
    [ThreadStatic] public static int SpreadDepth;

    public static bool Relevant(GameCondition c)
        => c?.def != null && DisasterFamily.Conditions.Contains(c.def.defName)
            && (c.gameConditionManager?.ownerMap == null || c.gameConditionManager.ownerMap.IsPlayerHome);

    public static string Id(GameCondition c) => "GameCondition_" + c.uniqueID;

    /// <summary>At the start of recording: the relevant conditions running now (the world's and every player home's).</summary>
    public static IEnumerable<(string, string)> ActiveNow()
    {
        var managers = new List<GameConditionManager>();
        if (Find.World?.gameConditionManager is { } w)
            managers.Add(w);
        foreach (var map in Find.Maps)
            if (map.IsPlayerHome)
                managers.Add(map.gameConditionManager);
        foreach (var m in managers)
            foreach (var c in m.ActiveConditions)
                if (Relevant(c))
                    yield return (c.def.defName, Id(c));
    }
}

[MetricAdapter(DisasterFamily.Name)]
[HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.RegisterCondition))]
public static class RegisterConditionMetricPatch
{
    public static void Postfix(GameConditionManager __instance, GameCondition cond)
    {
        if (cond == null || __instance == null || !LiveMetrics.IsRecordingUnit(DisasterFamily.Name))
            return;
        try
        {
            if (!__instance.ActiveConditions.Contains(cond) || !ConditionProbe.Relevant(cond))
                return;
            var def = cond.def.defName;
            var id = ConditionProbe.Id(cond);
            var tick = GameFacts.Tick;
            LiveMetrics.RecordOrDefer(DisasterFamily.Name, s => DisasterFamily.ConditionStarted(s, def, id, tick, counted: true));
        }
        catch (Exception e) { LiveMetrics.Fail(DisasterFamily.Name, e); }
    }
}

[MetricAdapter(DisasterFamily.Name)]
[HarmonyPatch(typeof(GameCondition), nameof(GameCondition.End))]
public static class EndConditionMetricPatch
{
    public static void Prefix(GameCondition __instance)
    {
        if (__instance?.def == null || !DisasterFamily.Conditions.Contains(__instance.def.defName)
            || !LiveMetrics.IsRecordingUnit(DisasterFamily.Name))
            return;
        try
        {
            var def = __instance.def.defName;
            var id = ConditionProbe.Id(__instance);
            var tick = GameFacts.Tick;
            LiveMetrics.RecordOrDefer(DisasterFamily.Name, s => DisasterFamily.ConditionEnded(s, def, id, tick));
        }
        catch (Exception e) { LiveMetrics.Fail(DisasterFamily.Name, e); }
    }
}

[MetricAdapter(DisasterFamily.Name, ModdedIncidentFamily.Name)]
[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
public static class DisasterIncidentMetricPatch
{
    public static void Prefix(IncidentWorker __instance, IncidentParms parms, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(DisasterFamily.Name) && !LiveMetrics.IsRecordingUnit(ModdedIncidentFamily.Name))
            return;
        try
        {
            __state = GameFacts.SkippedForNoColonists(__instance, parms);
        }
        catch (Exception e)
        {
            GameFacts.FailAll(e, DisasterFamily.Name, ModdedIncidentFamily.Name);
        }
    }

    public static void Postfix(bool __result, IncidentWorker __instance, IncidentParms parms, bool __state)
    {
        if (!__result || __state)
            return;
        if (!LiveMetrics.IsRecordingUnit(DisasterFamily.Name) && !LiveMetrics.IsRecordingUnit(ModdedIncidentFamily.Name))
            return;
        try
        {
            if (__instance?.def is not { } definition)
                return;

            // Preserve the exact legacy three-def counters, independent of incident provenance.
            if (definition.defName is "CropBlight" or "MeteoriteImpact" or "ShortCircuit")
                GameFacts.Record(DisasterFamily.Name, s => DisasterFamily.Incident(s, definition.defName));

            // The generic family is an independent opt-in. A successful player-home incident
            // is observed once at the outer TryExecute return, never during the worker attempt.
            if (parms?.target is not Map { IsPlayerHome: true })
                return;
            var packageId = definition.modContentPack?.PackageId;
            if (!ModdedIncidentFamily.IsModded(packageId, definition.defName))
                return;
            GameFacts.Record(ModdedIncidentFamily.Name, s =>
                ModdedIncidentFamily.RecordSuccess(s, packageId, definition.defName, succeeded: true, playerHome: true));
        }
        catch (Exception e)
        {
            GameFacts.FailAll(e, DisasterFamily.Name, ModdedIncidentFamily.Name);
        }
    }
}

[MetricAdapter(DisasterFamily.Name)]
[HarmonyPatch(typeof(Fire), "TrySpread")]
public static class FireSpreadScopeMetricPatch
{
    public static void Prefix() => ConditionProbe.SpreadDepth++;

    public static Exception? Finalizer(Exception? __exception)
    {
        ConditionProbe.SpreadDepth--;
        return __exception;
    }
}

[MetricAdapter(DisasterFamily.Name)]
[HarmonyPatch(typeof(FireUtility), nameof(FireUtility.TryStartFireIn))]
public static class FireStartMetricPatch
{
    public static void Prefix(IntVec3 c, Map map, out bool __state)
    {
        __state = false;
        if (ConditionProbe.SpreadDepth != 0 || !LiveMetrics.IsRecordingUnit(DisasterFamily.Name))
            return;
        try
        {
            if (map is not { IsPlayerHome: true })
                return;
            __state = c.InBounds(map) && !c.ContainsStaticFire(map);
        }
        catch (Exception e) { LiveMetrics.Fail(DisasterFamily.Name, e); }
    }

    public static void Postfix(bool __result, bool __state)
    {
        if (!__state || !__result)
            return;
        try
        {
            GameFacts.Record(DisasterFamily.Name, DisasterFamily.Fire);
        }
        catch (Exception e) { LiveMetrics.Fail(DisasterFamily.Name, e); }
    }
}

[MetricAdapter(DisasterFamily.Name)]
[HarmonyPatch(typeof(Thing), nameof(Thing.Kill))]
public static class BuildingBurnedMetricPatch
{
    public static void Prefix(Thing __instance, DamageInfo? dinfo, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(DisasterFamily.Name))
            return;
        try
        {
            __state = __instance is Building && !__instance.Destroyed && GameFacts.IsPlayer(__instance) && dinfo?.Def == DamageDefOf.Flame;
        }
        catch (Exception e) { LiveMetrics.Fail(DisasterFamily.Name, e); }
    }

    public static void Postfix(Thing __instance, bool __state)
    {
        if (!__state)
            return;
        try
        {
            if (__instance is { Destroyed: true })
                GameFacts.Record(DisasterFamily.Name, DisasterFamily.BuildingBurned);
        }
        catch (Exception e) { LiveMetrics.Fail(DisasterFamily.Name, e); }
    }
}
