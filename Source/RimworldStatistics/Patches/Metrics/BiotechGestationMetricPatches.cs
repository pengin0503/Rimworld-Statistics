using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// A gestator's forming-completed notification by itself does not prove a spawned
/// mech. Mark the candidate persistently; only a living player mech genuinely
/// ejected later becomes a counted manufacture. Resurrection is excluded.
/// </summary>
[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(Building_MechGestator), nameof(Building_MechGestator.Notify_FormingCompleted))]
public static class BiotechGestationReadyMetricPatch
{
    public static void Prefix(Building_MechGestator __instance, out (Pawn? Mech, bool Player, bool Resurrection) __state)
    {
        __state = default;
        try
        {
            if (__instance is null || !GameFacts.IsPlayerFaction(__instance.Faction)
                || !LiveMetrics.CanCaptureEvent(BiotechFamily.Name))
                return;
            __state = (__instance.GestatingMech,
                true,
                __instance.ResurrectingMechCorpse != null);
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }

    public static void Postfix((Pawn? Mech, bool Player, bool Resurrection) __state)
    {
        var mech = __state.Mech;
        if (mech is null)
            return;
        try
        {
            if (mech.Dead || !mech.RaceProps.IsMechanoid)
                return;
            var id = GameFacts.Id(mech);
            GameFacts.Record(BiotechFamily.Name, s =>
            {
                BiotechFamily.GestationReady(s, id, __state.Player, __state.Resurrection);
                // Notify_FormingCompleted may itself eject the pawn before its postfix.
                // Count only after the returned mech has actually entered a player map.
                if (mech.Spawned && GameFacts.IsPlayerFaction(mech.Faction))
                    BiotechFamily.GestationEjected(s, id, livingPlayerMech: true);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }
}

[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(Building_MechGestator), nameof(Building_MechGestator.EjectContents))]
public static class BiotechGestationEjectMetricPatch
{
    public static void Prefix(Building_MechGestator __instance, out Pawn? __state)
    {
        __state = null;
        try
        {
            if (__instance?.GestatingMech is not { } mech
                || (!GameFacts.IsPlayerFaction(__instance.Faction) && !GameFacts.IsPlayerFaction(mech.Faction))
                || !LiveMetrics.CanCaptureEvent(BiotechFamily.Name))
                return;
            __state = mech;
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }

    public static void Postfix(Pawn? __state)
    {
        var mech = __state;
        if (mech is null)
            return;
        try
        {
            if (mech.Dead || !mech.Spawned || !mech.RaceProps.IsMechanoid)
                return;
            GameFacts.Record(BiotechFamily.Name, s => BiotechFamily.GestationEjected(
                s, GameFacts.Id(mech), livingPlayerMech: GameFacts.IsPlayerFaction(mech.Faction)));
        }
        catch (Exception e) { LiveMetrics.Fail(BiotechFamily.Name, e); }
    }
}
