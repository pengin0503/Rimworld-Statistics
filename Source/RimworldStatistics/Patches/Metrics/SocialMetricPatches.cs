using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>Relations, fights, prison breaks and bonds (evidence #social). Only transitions involving the player's side count.</summary>
internal static class SocialProbe
{
    public static bool PlayerSide(Pawn? a, Pawn? b) => GameFacts.IsPlayer(a) || GameFacts.IsPlayer(b);

    public static string Pair(Pawn a, Pawn b)
    {
        var x = GameFacts.Id(a);
        var y = GameFacts.Id(b);
        return string.CompareOrdinal(x, y) <= 0 ? x + "|" + y : y + "|" + x;
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
public static class AddRelationMetricPatch
{
    public static void Prefix(Pawn_RelationsTracker __instance, Pawn ___pawn, PawnRelationDef def, Pawn otherPawn, out bool __state)
    {
        __state = true;
        if (!LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try
        {
            if (def == null || otherPawn == null || ___pawn == null || !SocialProbe.PlayerSide(___pawn, otherPawn))
                return;
            __state = __instance.DirectRelationExists(def, otherPawn);
        }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }

    public static void Postfix(Pawn_RelationsTracker __instance, Pawn ___pawn, PawnRelationDef def, Pawn otherPawn, bool __state)
    {
        if (__state || !LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try
        {
            if (def == null || otherPawn == null || ___pawn == null || !SocialProbe.PlayerSide(___pawn, otherPawn)
                || !LiveMetrics.CanCaptureEvent(SocialFamily.Name))
                return;
            if (!__instance.DirectRelationExists(def, otherPawn))
                return;
            var pair = SocialProbe.Pair(___pawn, otherPawn);
            var name = def.defName;
            var tick = GameFacts.Tick;
            GameFacts.Record(SocialFamily.Name, s => SocialFamily.Relation(s, name, pair, tick));
        }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.DoDivorce))]
public static class DivorceMetricPatch
{
    public static void Prefix(Pawn initiator, Pawn recipient, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try
        {
            if (initiator?.relations == null || recipient == null || !SocialProbe.PlayerSide(initiator, recipient))
                return;
            __state = initiator.relations.DirectRelationExists(PawnRelationDefOf.Spouse, recipient);
        }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }

    public static void Postfix(Pawn initiator, Pawn recipient, bool __state)
    {
        if (!__state || !LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try
        {
            if (initiator?.relations == null || recipient == null || !SocialProbe.PlayerSide(initiator, recipient)
                || !LiveMetrics.CanCaptureEvent(SocialFamily.Name))
                return;
            if (!initiator.relations.DirectRelationExists(PawnRelationDefOf.Spouse, recipient))
                GameFacts.Record(SocialFamily.Name, SocialFamily.Divorce);
        }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.StartSocialFight))]
public static class SocialFightMetricPatch
{
    /// <summary>MentalStateDef of 1.7.4985 Core.</summary>
    private const string SocialFighting = "SocialFighting";

    public static void Postfix(Pawn ___pawn, Pawn otherPawn)
    {
        if (!LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try
        {
            if (___pawn?.MentalStateDef?.defName == SocialFighting && otherPawn?.MentalStateDef?.defName == SocialFighting
                && SocialProbe.PlayerSide(___pawn, otherPawn))
                GameFacts.Record(SocialFamily.Name, SocialFamily.SocialFight);
        }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(PrisonBreakUtility), nameof(PrisonBreakUtility.StartPrisonBreak),
    new[] { typeof(Pawn), typeof(string), typeof(string), typeof(LetterDef), typeof(List<Pawn>) },
    new[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out })]
public static class PrisonBreakMetricPatch
{
    public static void Prefix(Pawn initiator, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try { __state = GameFacts.IsPrisonerOfColonySafe(initiator); }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }

    public static void Postfix(List<Pawn> escapingPrisoners, bool __state)
    {
        if (!__state || !LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try
        {
            var n = escapingPrisoners?.Count ?? 0;
            GameFacts.Record(SocialFamily.Name, s => SocialFamily.PrisonBreak(s, n));
        }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(RelationsUtility), nameof(RelationsUtility.TryDevelopBondRelation))]
public static class AnimalBondMetricPatch
{
    public static void Postfix(bool __result, Pawn humanlike, Pawn animal)
    {
        if (!__result || !LiveMetrics.IsRecordingUnit(SocialFamily.Name))
            return;
        try
        {
            if (GameFacts.IsPlayer(humanlike) && GameFacts.IsPlayer(animal))
                GameFacts.Record(SocialFamily.Name, SocialFamily.AnimalBond);
        }
        catch (Exception e) { LiveMetrics.Fail(SocialFamily.Name, e); }
    }
}
