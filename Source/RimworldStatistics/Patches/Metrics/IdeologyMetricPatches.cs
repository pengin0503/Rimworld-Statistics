using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// The actual before/after ideology transition is sampled; a request to convert is not a conversion.
/// The owning Pawn is injected from the tracker field and its colony status is evaluated after success.
/// </summary>
[MetricAdapter(IdeologyFamily.Name)]
[HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo), new[] { typeof(Ideo) })]
public static class PawnSetIdeoMetricPatch
{
    public static void Prefix(Pawn ___pawn, out Ideo? __state)
    {
        __state = null;
        if (LiveMetrics.Family(IdeologyFamily.Name) is null || ___pawn is null)
            return;
        try { __state = ___pawn.Ideo; }
        catch (Exception e) { LiveMetrics.Fail(IdeologyFamily.Name, e); }
    }

    public static void Postfix(Pawn ___pawn, Ideo? __state)
    {
        if (__state is null || ___pawn is null || LiveMetrics.Family(IdeologyFamily.Name) is null)
            return;
        try
        {
            var current = ___pawn.Ideo;
            if (current is null || ReferenceEquals(__state, current))
                return;
            var owned = ___pawn.IsFreeNonSlaveColonist || ___pawn.IsPrisonerOfColony;
            var active = ___pawn.Spawned || GameFacts.InPlayerCaravan(___pawn);
            // Stable scenario-created pawn ideologies are applied before world placement; exclude them.
            GameFacts.Record(IdeologyFamily.Name, s => IdeologyFamily.Converted(
                s, GameFacts.Id(___pawn), __state.GetUniqueLoadID(), current.GetUniqueLoadID(), owned, active));
        }
        catch (Exception e) { LiveMetrics.Fail(IdeologyFamily.Name, e); }
    }
}
