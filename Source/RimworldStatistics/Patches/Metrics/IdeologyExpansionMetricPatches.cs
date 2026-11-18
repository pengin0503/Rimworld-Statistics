using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Confirmed prisoner-to-slave transition (<c>GenGuest.TryEnslavePrisoner</c>): both <c>InteractionWorker_EnslaveAttempt</c>
/// and <c>GenGuest.EnslavePrisoner</c> converge here in 1.7.5011. Only a pawn who was a colony prisoner before the call and
/// is a colony slave with <c>__result == true</c> afterwards counts.
/// </summary>
[MetricAdapter(IdeologyExpansionFamily.Name)]
[HarmonyPatch(typeof(GenGuest), nameof(GenGuest.TryEnslavePrisoner))]
public static class PrisonerEnslavedMetricPatch
{
    public static void Prefix(Pawn prisoner, out bool __state)
    {
        __state = false;
        if (!UnityData.IsInMainThread || Scribe.mode != LoadSaveMode.Inactive)
            return;
        try
        {
            if (!LiveMetrics.CanCaptureEvent(IdeologyExpansionFamily.Name))
                return;
            __state = GameFacts.IsPrisonerOfColonySafe(prisoner) && !GameFacts.IsSlaveOfColonySafe(prisoner);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(IdeologyExpansionFamily.Name, e);
        }
    }

    public static void Postfix(bool __result, Pawn prisoner, bool __state)
    {
        if (!__state || !__result)
            return;
        try
        {
            if (!GameFacts.IsSlaveOfColonySafe(prisoner))
                return;
            GameFacts.Record(IdeologyExpansionFamily.Name, s =>
                IdeologyExpansionFamily.PrisonerEnslaved(s, wasColonyPrisoner: true, nowColonySlave: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(IdeologyExpansionFamily.Name, e);
        }
    }
}

/// <summary>
/// Confirmed biosculpter pod cycle completion (<c>CompBiosculpterPod.CycleCompleted</c>, 1.7.5011):
/// cancelled or power-interrupted ejections call <c>EjectContents(interrupted: true, ...)</c> directly without entering
/// <c>CycleCompleted</c>. Prefix captures the active cycle key and player-side occupant before <c>CycleCompleted</c>
/// ejects the pawn and clears <c>currentCycleKey</c>; Postfix verifies the occupant survived and was ejected.
/// </summary>
[MetricAdapter(IdeologyExpansionFamily.Name)]
[HarmonyPatch(typeof(CompBiosculpterPod), "CycleCompleted")]
public static class BiosculpterCycleCompletedMetricPatch
{
    public readonly record struct PendingCycle(Pawn Occupant, string? CycleKey);

    public static void Prefix(CompBiosculpterPod __instance, out PendingCycle? __state)
    {
        __state = null;
        if (!UnityData.IsInMainThread || Scribe.mode != LoadSaveMode.Inactive)
            return;
        try
        {
            if (!LiveMetrics.CanCaptureEvent(IdeologyExpansionFamily.Name))
                return;
            if (!GameFacts.IsPlayer(__instance?.parent) || __instance!.Occupant is not { Dead: false } occ)
                return;
            if (!GameFacts.IsPlayerFaction(occ.Faction) && !GameFacts.IsPrisonerOfColonySafe(occ))
                return;
            __state = new PendingCycle(occ, __instance.CurrentCycle?.Props?.key);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(IdeologyExpansionFamily.Name, e);
        }
    }

    public static void Postfix(CompBiosculpterPod __instance, PendingCycle? __state)
    {
        if (__state is not { } pending || __instance is null)
            return;
        try
        {
            if (pending.Occupant.Dead || __instance.Occupant != null)
                return;
            GameFacts.Record(IdeologyExpansionFamily.Name, s =>
                IdeologyExpansionFamily.BiosculpterCycleCompleted(
                    s,
                    playerPod: true,
                    colonyOccupant: true,
                    pending.CycleKey));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(IdeologyExpansionFamily.Name, e);
        }
    }
}
