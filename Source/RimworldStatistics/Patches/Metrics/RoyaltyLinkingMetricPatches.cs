using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Confirmed psylink linking ritual completion (<c>CompPsylinkable.FinishLinkingRitual</c>, 1.7.5011):
/// Prefix records the player-side pawn's psylink level before the ritual completes, and Postfix confirms
/// that the pawn survived and actually gained at least one psylink level.
/// </summary>
[MetricAdapter(RoyaltyLinkingFamily.Name)]
[HarmonyPatch(typeof(CompPsylinkable), nameof(CompPsylinkable.FinishLinkingRitual))]
public static class AnimaTreeLinkingMetricPatch
{
    public static void Prefix(Pawn pawn, out int? __state)
    {
        __state = null;
        if (!UnityData.IsInMainThread || Scribe.mode != LoadSaveMode.Inactive)
            return;
        try
        {
            if (!LiveMetrics.CanCaptureEvent(RoyaltyLinkingFamily.Name))
                return;
            if (pawn is not { Dead: false })
                return;
            if (!GameFacts.IsPlayerFaction(pawn.Faction) && !GameFacts.IsPrisonerOfColonySafe(pawn))
                return;
            __state = pawn.GetPsylinkLevel();
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(RoyaltyLinkingFamily.Name, e);
        }
    }

    public static void Postfix(Pawn pawn, int? __state)
    {
        if (__state is not { } beforeLevel || pawn is not { Dead: false })
            return;
        try
        {
            var afterLevel = pawn.GetPsylinkLevel();
            GameFacts.Record(RoyaltyLinkingFamily.Name, s =>
                RoyaltyLinkingFamily.AnimaLinkingCompleted(
                    s,
                    colonyPawn: true,
                    beforeLevel,
                    afterLevel));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(RoyaltyLinkingFamily.Name, e);
        }
    }
}
