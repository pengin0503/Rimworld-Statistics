using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>Taming (evidence #animals): the recruitment of an animal that was wild before and the player's afterwards.</summary>
[MetricAdapter(AnimalsFamily.Name)]
[HarmonyPatch(typeof(InteractionWorker_RecruitAttempt), nameof(InteractionWorker_RecruitAttempt.DoRecruit),
    new[] { typeof(Pawn), typeof(Pawn), typeof(string), typeof(string), typeof(bool), typeof(bool) },
    new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
public static class TameMetricPatch
{
    public static void Prefix(Pawn recruitee, out bool __state)
    {
        __state = false;
        try { __state = recruitee != null && recruitee.RaceProps.Animal && recruitee.Faction == null; }
        catch (Exception e) { LiveMetrics.Fail(AnimalsFamily.Name, e); }
    }

    public static void Postfix(Pawn recruitee, bool __state)
    {
        if (!__state)
            return;
        try
        {
            if (recruitee == null || !recruitee.RaceProps.Animal || !GameFacts.IsPlayerFaction(recruitee.Faction))
                return;
            GameFacts.Record(AnimalsFamily.Name, s => AnimalsFamily.Tamed(s, wasWild: true, nowPlayer: true));
        }
        catch (Exception e) { LiveMetrics.Fail(AnimalsFamily.Name, e); }
    }
}

/// <summary>Births and hatchings (evidence #animals): one successful spawn per pawn; CompHatcher ends here too.</summary>
[MetricAdapter(AnimalsFamily.Name)]
[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn))]
public static class AnimalBornMetricPatch
{
    public static void Postfix(bool __result, Pawn pawn)
    {
        if (!__result)
            return;
        try
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Animal || !GameFacts.IsPlayerFaction(pawn.Faction))
                return;
            GameFacts.Record(AnimalsFamily.Name, s => AnimalsFamily.Born(s, true));
        }
        catch (Exception e) { LiveMetrics.Fail(AnimalsFamily.Name, e); }
    }
}
