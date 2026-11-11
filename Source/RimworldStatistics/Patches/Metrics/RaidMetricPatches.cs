using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Raids and threat incidents (evidence #raid). An incident counts only when IncidentWorker.TryExecute returns true; a raid's
/// pawns are those TryGenerateRaidInfo returned inside that call (a debug test generation outside it is ignored), held until the
/// outer call succeeds. Scopes are a stack on the game thread.
/// </summary>
internal static class RaidProbe
{
    public sealed class Scope
    {
        public List<string>? Pawns;
    }

    public static readonly List<Scope> Stack = new();
}

[MetricAdapter(RaidFamily.Name)]
[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
public static class IncidentExecuteMetricPatch
{
    public static void Prefix(out object? __state)
    {
        __state = null;
        if (!LiveMetrics.CanCaptureEvent(RaidFamily.Name))
            return;
        var scope = new RaidProbe.Scope();
        RaidProbe.Stack.Add(scope);
        __state = scope;
    }

    public static void Postfix(bool __result, IncidentWorker __instance, IncidentParms parms, object? __state)
    {
        if (__state is not RaidProbe.Scope scope || !__result || __instance?.def == null)
            return;
        var tick = GameFacts.Tick;
        var def = __instance.def.defName;
        GameFacts.Record(RaidFamily.Name, s =>
        {
            RaidFamily.Incident(s, def);
            if (__instance is IncidentWorker_RaidEnemy && scope.Pawns != null && parms?.faction != null
                && Faction.OfPlayer != null && parms.faction.HostileTo(Faction.OfPlayer))
            {
                RaidFamily.Arrived(s, new RaidArrival
                {
                    MapId = GameFacts.MapId(parms.target as Map), PawnIds = scope.Pawns, Points = parms.points,
                    ArrivalMode = parms.raidArrivalMode?.defName, Strategy = parms.raidStrategy?.defName, FactionDef = parms.faction.def?.defName,
                }, tick);
            }
        });
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
        RaidProbe.Stack[^1].Pawns = pawns.Where(p => p != null).Select(GameFacts.Id).ToList();
    }
}

/// <summary>A pawn left its lord (evidence #raid: the loss condition and the pawn's absence from the lord afterwards).</summary>
[MetricAdapter(RaidFamily.Name)]
[HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnLost))]
public static class LordPawnLostMetricPatch
{
    public static void Postfix(Lord __instance, Pawn pawn, PawnLostCondition cond)
    {
        if (pawn == null || __instance.ownedPawns.Contains(pawn))
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
        GameFacts.Record(RaidFamily.Name, s => RaidFamily.MemberLost(s, id, loss, tick));
    }
}

[MetricAdapter(RaidFamily.Name)]
[HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
public static class MapRemovedMetricPatch
{
    public static void Prefix(Map map)
    {
        if (map == null)
            return;
        var id = GameFacts.MapId(map);
        var tick = GameFacts.Tick;
        GameFacts.Record(RaidFamily.Name, s => RaidFamily.MapRemoved(s, id, tick));
    }
}
