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
    public static void Prefix(Pawn recruitee, out bool __state) => __state = recruitee != null && recruitee.RaceProps.Animal && recruitee.Faction == null;

    public static void Postfix(Pawn recruitee, bool __state)
    {
        if (recruitee == null || !recruitee.RaceProps.Animal)
            return;
        var now = recruitee.Faction == Faction.OfPlayer;
        GameFacts.Record(AnimalsFamily.Name, s => AnimalsFamily.Tamed(s, __state, now));
    }
}

/// <summary>Births and hatchings (evidence #animals): one successful spawn per pawn; CompHatcher ends here too.</summary>
[MetricAdapter(AnimalsFamily.Name)]
[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn))]
public static class AnimalBornMetricPatch
{
    public static void Postfix(bool __result, Pawn pawn)
    {
        if (!__result || pawn == null || pawn.Dead || !pawn.RaceProps.Animal)
            return;
        var player = pawn.Faction == Faction.OfPlayer;
        GameFacts.Record(AnimalsFamily.Name, s => AnimalsFamily.Born(s, player));
    }
}
