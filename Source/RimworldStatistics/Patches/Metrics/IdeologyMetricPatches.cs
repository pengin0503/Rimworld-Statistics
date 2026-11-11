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
        if (!LiveMetrics.IsRecordingNow(IdeologyFamily.Name) || ___pawn is null)
            return;
        try { __state = ___pawn.Ideo; }
        catch (Exception e) { LiveMetrics.Fail(IdeologyFamily.Name, e); }
    }

    public static void Postfix(Pawn ___pawn, Ideo? __state)
    {
        if (__state is null || ___pawn is null || !LiveMetrics.IsRecordingNow(IdeologyFamily.Name))
            return;
        try
        {
            var current = ___pawn.Ideo;
            if (current is null || ReferenceEquals(__state, current))
                return;
            var owned = ___pawn.IsFreeNonSlaveColonist || ___pawn.IsPrisonerOfColony;
            var active = ___pawn.Spawned || GameFacts.InPlayerCaravan(___pawn);
            // Stable scenario-created pawn ideologies are applied before world placement; exclude them.
            var id = GameFacts.Id(___pawn);
            var destination = current.GetUniqueLoadID();
            GameFacts.Record(IdeologyFamily.Name, s =>
            {
                IdeologyFamily.Converted(s, id, __state.GetUniqueLoadID(), destination, owned, active);
                // Only a matching, previously observed explicit attempt contributes
                // to conversion duration. Unrelated changes do not start timers.
                if (owned && active)
                    IdeologyFamily.ConversionReachedTarget(s, id, destination, GameFacts.Tick, eligible: true);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(IdeologyFamily.Name, e); }
    }
}

    /// <summary>
    /// This game API is a real conversion action, not GUI targeting. It is
    /// invoked for a known recipient and target ideology. A failed attempt
    /// starts an interval only; a later SetIdeo to that target closes it.
    /// </summary>
    [MetricAdapter(IdeologyFamily.Name)]
    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.IdeoConversionAttempt))]
    public static class IdeologyExplicitAttemptMetricPatch
    {
        public static void Prefix(Pawn ___pawn, Ideo initiatorIdeo)
        {
            if (!LiveMetrics.IsRecordingNow(IdeologyFamily.Name) || ___pawn is null
                || initiatorIdeo is null || ReferenceEquals(___pawn.Ideo, initiatorIdeo))
                return;
            var eligible = ___pawn.IsFreeNonSlaveColonist || ___pawn.IsPrisonerOfColony;
            if (!eligible || (!___pawn.Spawned && !GameFacts.InPlayerCaravan(___pawn)))
                return;
            var id = GameFacts.Id(___pawn);
            GameFacts.Record(IdeologyFamily.Name, s => IdeologyFamily.ConversionActionStarted(
                s, id, initiatorIdeo.GetUniqueLoadID(), GameFacts.Tick,
                eligible: true, explicitAction: true));
        }
    }
