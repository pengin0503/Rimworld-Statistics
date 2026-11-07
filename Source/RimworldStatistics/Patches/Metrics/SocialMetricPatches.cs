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
    public static void Prefix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn, out bool __state)
        => __state = def != null && otherPawn != null && __instance.DirectRelationExists(def, otherPawn);

    public static void Postfix(Pawn_RelationsTracker __instance, Pawn ___pawn, PawnRelationDef def, Pawn otherPawn, bool __state)
    {
        if (__state || def == null || otherPawn == null || ___pawn == null || !__instance.DirectRelationExists(def, otherPawn)
            || !SocialProbe.PlayerSide(___pawn, otherPawn))
            return;
        var pair = SocialProbe.Pair(___pawn, otherPawn);
        var name = def.defName;
        var tick = GameFacts.Tick;
        GameFacts.Record(SocialFamily.Name, s => SocialFamily.Relation(s, name, pair, tick));
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.DoDivorce))]
public static class DivorceMetricPatch
{
    public static void Prefix(Pawn initiator, Pawn recipient, out bool __state)
        => __state = initiator?.relations != null && recipient != null && initiator.relations.DirectRelationExists(PawnRelationDefOf.Spouse, recipient);

    public static void Postfix(Pawn initiator, Pawn recipient, bool __state)
    {
        if (__state && !initiator.relations.DirectRelationExists(PawnRelationDefOf.Spouse, recipient) && SocialProbe.PlayerSide(initiator, recipient))
            GameFacts.Record(SocialFamily.Name, SocialFamily.Divorce);
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
        if (___pawn?.MentalStateDef?.defName == SocialFighting && otherPawn?.MentalStateDef?.defName == SocialFighting
            && SocialProbe.PlayerSide(___pawn, otherPawn))
            GameFacts.Record(SocialFamily.Name, SocialFamily.SocialFight);
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(PrisonBreakUtility), nameof(PrisonBreakUtility.StartPrisonBreak),
    new[] { typeof(Pawn), typeof(string), typeof(string), typeof(LetterDef), typeof(List<Pawn>) },
    new[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out })]
public static class PrisonBreakMetricPatch
{
    public static void Prefix(Pawn initiator, out bool __state) => __state = initiator != null && initiator.IsPrisonerOfColony;

    public static void Postfix(List<Pawn> escapingPrisoners, bool __state)
    {
        if (!__state)
            return;
        var n = escapingPrisoners?.Count ?? 0;
        GameFacts.Record(SocialFamily.Name, s => SocialFamily.PrisonBreak(s, n));
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(RelationsUtility), nameof(RelationsUtility.TryDevelopBondRelation))]
public static class AnimalBondMetricPatch
{
    public static void Postfix(bool __result, Pawn humanlike, Pawn animal)
    {
        if (__result && GameFacts.IsPlayer(humanlike) && GameFacts.IsPlayer(animal))
            GameFacts.Record(SocialFamily.Name, SocialFamily.AnimalBond);
    }
}
