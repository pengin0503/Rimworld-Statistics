using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Completed Ideology rituals use the saved Lord ID to deduplicate across
/// calls/reloads. Quality-based rituals expose their *selected* outcome via
/// FromQuality.GetOutcome; the preview, expected-quality UI and cancelled
/// targets must not count. Non-quality outcomes remain unclassified rather than
/// being spuriously treated as successful.
/// </summary>
internal static class IdeologyRitualFacts
{
    private static readonly FieldInfo? LordField = AccessTools.Field(typeof(LordJob), "lord");

    public static string? Id(LordJob_Ritual job)
    {
        if (LordField?.GetValue(job) is not Lord lord)
            return null;
        return lord.GetUniqueLoadID();
    }

    public static bool IsPlayerRitual(LordJob_Ritual job)
        => job?.Ritual != null && LordField?.GetValue(job) is Lord lord
            && lord.faction == Faction.OfPlayer;

    [ThreadStatic] private static LordJob_Ritual? active;
    [ThreadStatic] private static RitualOutcomePossibility? selected;

    public static void Enter(LordJob_Ritual job)
    {
        active = job;
        selected = null;
    }

    public static void Select(LordJob_Ritual job, RitualOutcomePossibility? outcome)
    {
        if (ReferenceEquals(active, job))
            selected = outcome;
    }

    public static void Exit(bool succeeded)
    {
        var job = active;
        var outcome = selected;
        active = null;
        selected = null;
        if (!succeeded || job is null || outcome is null || Id(job) is not { } id)
            return;
        // RimWorld's actual chosen outcome has a Positive property. Use a
        // stable polarity bucket, not the translated outcome label or a guess
        // based on expected ritual quality.
        var positive = outcome.Positive;
        GameFacts.Record(IdeologyFamily.Name, s => IdeologyFamily.RitualConcluded(
            s, id, positive ? "positive" : "non_positive",
            positive, completed: true, interrupted: false));
    }
}

[MetricAdapter(IdeologyFamily.Name)]
[HarmonyPatch(typeof(RitualOutcomeEffectWorker_FromQuality),
    nameof(RitualOutcomeEffectWorker_FromQuality.Apply))]
public static class IdeologyRitualOutcomeApplyMetricPatch
{
    public static void Prefix(LordJob_Ritual jobRitual, out bool __state)
    {
        __state = LiveMetrics.IsRecordingNow(IdeologyFamily.Name)
            && jobRitual is not null && IdeologyRitualFacts.IsPlayerRitual(jobRitual);
        if (__state && jobRitual is not null)
            IdeologyRitualFacts.Enter(jobRitual);
    }

    public static Exception? Finalizer(bool __state, Exception? __exception)
    {
        if (__state)
            IdeologyRitualFacts.Exit(__exception is null);
        return __exception;
    }
}

[MetricAdapter(IdeologyFamily.Name)]
[HarmonyPatch(typeof(RitualOutcomeEffectWorker_FromQuality),
    nameof(RitualOutcomeEffectWorker_FromQuality.GetOutcome))]
public static class IdeologySelectedRitualOutcomeMetricPatch
{
    public static void Postfix(LordJob_Ritual ritual, RitualOutcomePossibility __result)
    {
        if (__result != null && ritual != null)
            IdeologyRitualFacts.Select(ritual, __result);
    }
}

[MetricAdapter(IdeologyFamily.Name)]
[HarmonyPatch(typeof(LordJob_Ritual), nameof(LordJob_Ritual.ApplyOutcome))]
public static class IdeologyOtherCompletedRitualMetricPatch
{
    public static void Postfix(LordJob_Ritual __instance, bool cancelled)
    {
        if (cancelled || !LiveMetrics.IsRecordingNow(IdeologyFamily.Name)
            || !IdeologyRitualFacts.IsPlayerRitual(__instance)
            || IdeologyRitualFacts.Id(__instance) is not { } id)
            return;
        // This is a completed event, but its outcome may not have a
        // quality selector. The earlier classified record wins by Lord ID.
        GameFacts.Record(IdeologyFamily.Name, s => IdeologyFamily.RitualConcluded(
            s, id, null, null, completed: true, interrupted: false));
    }
}

[MetricAdapter(IdeologyFamily.Name)]
[HarmonyPatch(typeof(LordJob_Ritual), "RitualFinished")]
public static class IdeologyInterruptedRitualMetricPatch
{
    public static void Postfix(LordJob_Ritual __instance, bool cancelled)
    {
        if (!cancelled || !LiveMetrics.IsRecordingNow(IdeologyFamily.Name)
            || !IdeologyRitualFacts.IsPlayerRitual(__instance)
            || IdeologyRitualFacts.Id(__instance) is not { } id)
            return;
        GameFacts.Record(IdeologyFamily.Name, s => IdeologyFamily.RitualConcluded(
            s, id, null, null, completed: false, interrupted: true));
    }
}
