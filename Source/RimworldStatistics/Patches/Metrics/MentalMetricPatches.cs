using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Phase C · mental (evidence #mental). A break counts only when TryDoMentalBreak succeeds inside
/// TryDoRandomMoodCausedMentalBreak called from the pawn's own MentalBreakerTickInterval: the other callers of
/// TryDoMentalBreak are drugs, genes, books and entities, and the dev tool "(natural mood break)" calls the mood method directly
/// (1.7.4985 DebugToolsPawns). The scopes are depth counters on the game thread.
/// </summary>
internal static class MentalProbe
{
    public const string Unit = MentalFamily.Name;
    public static int TickDepth;
    public static int MoodDepth;

    /// <summary>The reason text of the dev tool that grants an inspiration (1.7.4985 DebugToolsPawns).</summary>
    public const string DebugInspirationReason = "Debug gain";
}

[MetricAdapter(MentalFamily.Name)]
[HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.MentalBreakerTickInterval))]
public static class MentalBreakerTickMetricPatch
{
    public static void Prefix() => MentalProbe.TickDepth++;

    public static Exception? Finalizer(Exception? __exception)
    {
        MentalProbe.TickDepth--;
        return __exception;
    }
}

[MetricAdapter(MentalFamily.Name)]
[HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.TryDoRandomMoodCausedMentalBreak))]
public static class MoodBreakMetricPatch
{
    public static void Prefix() => MentalProbe.MoodDepth++;

    public static Exception? Finalizer(Exception? __exception)
    {
        MentalProbe.MoodDepth--;
        return __exception;
    }
}

[MetricAdapter(MentalFamily.Name)]
[HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.TryDoMentalBreak))]
public static class MentalBreakMetricPatch
{
    public static void Postfix(bool __result, MentalBreakDef breakDef, Pawn ___pawn)
    {
        if (LiveMetrics.Family(MentalProbe.Unit) is not { } s || breakDef is null || ___pawn is null)
            return;
        try
        {
            MentalFamily.Break(s, breakDef.defName, success: __result && ___pawn.InMentalState,
                moodCaused: MentalProbe.TickDepth > 0 && MentalProbe.MoodDepth > 0, freeColonist: ___pawn.IsFreeNonSlaveColonist);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MentalProbe.Unit, e);
        }
    }
}

[MetricAdapter(MentalFamily.Name)]
[HarmonyPatch(typeof(InspirationHandler), nameof(InspirationHandler.TryStartInspiration))]
public static class InspirationMetricPatch
{
    public static void Postfix(bool __result, InspirationDef def, string reason, InspirationHandler __instance)
    {
        if (LiveMetrics.Family(MentalProbe.Unit) is not { } s || def is null || __instance?.pawn is not { } pawn)
            return;
        try
        {
            MentalFamily.Inspiration(s, def.defName, success: __result, started: __instance.CurStateDef == def,
                freeColonist: pawn.IsFreeNonSlaveColonist, debug: reason == MentalProbe.DebugInspirationReason);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MentalProbe.Unit, e);
        }
    }
}
