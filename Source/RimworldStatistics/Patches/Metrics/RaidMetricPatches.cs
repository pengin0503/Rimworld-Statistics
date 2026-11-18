using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Raids and threat incidents (evidence #raid). An incident counts only when IncidentWorker.TryExecute returns true after running
/// the incident (not the true it returns for a map without colonists, #352); a raid's
/// pawns are those TryGenerateRaidInfo returned inside that call (a debug test generation outside it is ignored), held until the
/// outer call succeeds. Scopes are a stack on the game thread.
/// </summary>
internal static class RaidProbe
{
    public sealed class Scope
    {
        public List<string>? Pawns;

        /// <summary>The call returns true without running the incident (no colonists on the map, #352).</summary>
        public bool SkippedForNoColonists;
    }

    [ThreadStatic] private static List<Scope>? stack;

    public static List<Scope> Stack => stack ??= new List<Scope>();
}

[MetricAdapter(RaidFamily.Name)]
[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
public static class IncidentExecuteMetricPatch
{
    public static void Prefix(IncidentWorker __instance, IncidentParms parms, out object? __state)
    {
        __state = null;
        if (!LiveMetrics.CanCaptureEvent(RaidFamily.Name))
            return;
        try
        {
            var scope = new RaidProbe.Scope { SkippedForNoColonists = GameFacts.SkippedForNoColonists(__instance, parms) };
            RaidProbe.Stack.Add(scope);
            __state = scope;
        }
        catch (Exception e) { LiveMetrics.Fail(RaidFamily.Name, e); }
    }

    public static void Postfix(bool __result, IncidentWorker __instance, IncidentParms parms, object? __state)
    {
        if (__state is not RaidProbe.Scope scope || !__result || scope.SkippedForNoColonists || __instance?.def == null)
            return;
        try
        {
            var tick = GameFacts.Tick;
            var def = __instance.def.defName;
            var isEnemyRaid = __instance is IncidentWorker_RaidEnemy && scope.Pawns != null && parms?.faction != null
                && parms.faction.HostileTo(GameFacts.PlayerFaction);
            RaidArrival? arrival = isEnemyRaid
                ? new RaidArrival
                {
                    MapId = GameFacts.MapId(parms!.target as Map),
                    PawnIds = scope.Pawns!,
                    Points = parms.points,
                    ArrivalMode = parms.raidArrivalMode?.defName,
                    Strategy = parms.raidStrategy?.defName,
                    FactionDef = parms.faction.def?.defName,
                }
                : null;
            GameFacts.Record(RaidFamily.Name, s =>
            {
                RaidFamily.Incident(s, def);
                if (arrival != null)
                    RaidFamily.Arrived(s, arrival, tick);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(RaidFamily.Name, e); }
    }

    public static Exception? Finalizer(Exception? __exception, object? __state)
    {
        if (__state is RaidProbe.Scope scope)
            RaidProbe.Stack.Remove(scope);
        return __exception;
    }
}

[MetricAdapter(RaidFamily.Name)]
[HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
public static class RaidGenerateMetricPatch
{
    public static void Postfix(bool __result, List<Pawn> pawns, bool debugTest)
    {
        if (!__result || debugTest || pawns == null || RaidProbe.Stack.Count == 0)
            return;
        try
        {
            RaidProbe.Stack[^1].Pawns = pawns.Where(p => p != null).Select(GameFacts.Id).ToList();
        }
        catch (Exception e) { LiveMetrics.Fail(RaidFamily.Name, e); }
    }
}

/// <summary>A pawn left its lord (evidence #raid: the loss condition and the pawn's absence from the lord afterwards).</summary>
[MetricAdapter(RaidFamily.Name)]
[HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnLost))]
public static class LordPawnLostMetricPatch
{
    public static void Postfix(Lord __instance, Pawn pawn, PawnLostCondition cond)
    {
        if (pawn == null || __instance == null || !LiveMetrics.IsRecordingUnit(RaidFamily.Name))
            return;
        try
        {
            if (__instance.ownedPawns.Contains(pawn))
                return;   // the lord job kept the pawn
            var tick = GameFacts.Tick;
            if (cond == PawnLostCondition.Killed && PawnKillMetricPatch.TryDeferRaidLoss(pawn, tick))
                return;   // Pawn.Kill will credit the confirmed kill before applying the loss.
            var id = GameFacts.Id(pawn);
            var loss = cond switch
            {
                PawnLostCondition.Killed => RaidLoss.Killed,
                PawnLostCondition.ExitedMap or PawnLostCondition.LeftVoluntarily => RaidLoss.Exited,
                PawnLostCondition.MadePrisoner or PawnLostCondition.MadeSlave => RaidLoss.Captured,
                PawnLostCondition.Incapped => RaidLoss.Downed,
                _ => RaidLoss.Other,
            };
            LiveMetrics.RecordOrDefer(RaidFamily.Name, s => RaidFamily.MemberLost(s, id, loss, tick));
        }
        catch (Exception e) { LiveMetrics.Fail(RaidFamily.Name, e); }
    }
}

[MetricAdapter(RaidFamily.Name)]
[HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
public static class MapRemovedMetricPatch
{
    public static void Prefix(Map map)
    {
        if (map == null || !LiveMetrics.IsRecordingUnit(RaidFamily.Name))
            return;
        try
        {
            var id = GameFacts.MapId(map);
            var tick = GameFacts.Tick;
            LiveMetrics.RecordOrDefer(RaidFamily.Name, s => RaidFamily.MapRemoved(s, id, tick));
        }
        catch (Exception e) { LiveMetrics.Fail(RaidFamily.Name, e); }
    }
}
