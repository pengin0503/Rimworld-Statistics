using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Biotech child milestone: observe a genuine transition produced by biological
/// ageing, not a pawn's initial life-stage setup. A state comparison is cheap and
/// occurs only when this development-only family has been explicitly opened.
/// </summary>
[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(Pawn_AgeTracker), "TickBiologicalAge", new[] { typeof(int) })]
public static class BiotechChildStageMetricPatch
{
    public static void Prefix(Pawn_AgeTracker __instance, Pawn ___pawn, out int __state)
    {
        __state = -1;
        if (!LiveMetrics.IsRecordingNow(BiotechFamily.Name) || ___pawn is null || ___pawn.Dead
            || !___pawn.IsFreeNonSlaveColonist || !___pawn.RaceProps.Humanlike
            || __instance.AgeBiologicalYearsFloat >= 18f)
            return;
        __state = __instance.CurLifeStageIndex;
    }

    public static void Postfix(Pawn_AgeTracker __instance, Pawn ___pawn, int __state)
    {
        if (__state < 0 || ___pawn is null || ___pawn.Dead || !___pawn.IsFreeNonSlaveColonist
            || __instance.CurLifeStageIndex == __state)
            return;
        var newStage = __instance.CurLifeStage?.defName;
        if (string.IsNullOrWhiteSpace(newStage))
            return;
        GameFacts.Record(BiotechFamily.Name, s => BiotechFamily.ChildReachedStage(
            s, GameFacts.Id(___pawn), newStage, playerChild: true, confirmedNewStage: true));
    }
}

/// <summary>Mech repair completion is the disappearance of the *last* injury across a confirmed repair tick.</summary>
internal static class MechRepairMetricFacts
{
    public static bool WasInjured(Pawn mech)
        => LiveMetrics.IsRecordingNow(BiotechFamily.Name)
           && mech is { Dead: false } && mech.Faction == Faction.OfPlayer && mech.RaceProps.IsMechanoid
           && mech.health?.hediffSet?.hediffs?.Any(h => h is Hediff_Injury) == true;

    public static void Repaired(Pawn mech, bool hadInjury)
    {
        if (!hadInjury || mech is null || mech.Dead || mech.Faction != Faction.OfPlayer
            || mech.health?.hediffSet?.hediffs?.Any(h => h is Hediff_Injury) == true)
            return;
        // Both overloads may run for the same repair and same tick: deterministic
        // operation ID keeps it one event. A future new repair has a new tick.
        var operationId = GameFacts.Id(mech) + "/t" + GameFacts.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
        GameFacts.Record(BiotechFamily.Name, s => BiotechFamily.MechFullyRepaired(
            s, operationId, colonyMech: true, clearedLastInjury: true));
    }
}

[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn) })]
public static class BiotechMechRepairSimpleMetricPatch
{
    public static void Prefix(Pawn mech, out bool __state)
        => __state = MechRepairMetricFacts.WasInjured(mech);

    public static void Postfix(Pawn mech, bool __state)
        => MechRepairMetricFacts.Repaired(mech, __state);
}

[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn), typeof(int) })]
public static class BiotechMechRepairDeltaMetricPatch
{
    public static void Prefix(Pawn mech, out bool __state)
        => __state = MechRepairMetricFacts.WasInjured(mech);

    public static void Postfix(Pawn mech, bool __state)
        => MechRepairMetricFacts.Repaired(mech, __state);
}
