using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// The patch is gated until the 1.7 in-game probe confirms SetTitle's overload and actual title change.
/// The injected pawn is the owning Pawn_RoyaltyTracker.pawn field.
/// </summary>
[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.SetTitle))]
public static class RoyaltySetTitleMetricPatch
{
    public static void Prefix(Pawn_RoyaltyTracker __instance, Pawn ___pawn, Faction faction, out (int?, bool) __state)
    {
        __state = default;
        if (LiveMetrics.Family(RoyaltyFamily.Name) is null || ___pawn is null || faction is null)
            return;
        try
        {
            var eligible = ___pawn.IsFreeNonSlaveColonist && faction.def.HasRoyalTitles;
            var rank = __instance.GetCurrentTitleInFaction(faction)?.def.seniority;
            __state = (rank, eligible);
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }

    public static void Postfix(Pawn_RoyaltyTracker __instance, Faction faction, RoyalTitleDef title, (int?, bool) __state)
    {
        if (!__state.Item2 || faction is null)
            return;
        try
        {
            var now = __instance.GetCurrentTitleInFaction(faction)?.def;
            // Use the resulting title; an attempted grant that failed must not count.
            if (now != title || now is null || __state.Item1 == now.seniority)
                return;
            GameFacts.Record(RoyaltyFamily.Name, s =>
                RoyaltyFamily.TitleChanged(s, now.defName, __state.Item1, now.seniority, eligible: true));
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }
}

/// <summary>Only increases from actual GainFavor, including modded award paths that use the game API.</summary>
[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.GainFavor))]
public static class RoyaltyGainHonorMetricPatch
{
    public static void Prefix(Pawn_RoyaltyTracker __instance, Pawn ___pawn, Faction faction, out (int Before, bool Eligible) __state)
    {
        __state = default;
        if (LiveMetrics.Family(RoyaltyFamily.Name) is null || ___pawn is null || faction is null)
            return;
        try { __state = (__instance.GetFavor(faction), ___pawn.IsFreeNonSlaveColonist && faction.def.HasRoyalTitles); }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }

    public static void Postfix(Pawn_RoyaltyTracker __instance, Faction faction, (int Before, bool Eligible) __state)
    {
        if (!__state.Eligible)
            return;
        try
        {
            var after = __instance.GetFavor(faction);
            GameFacts.Record(RoyaltyFamily.Name, s =>
                RoyaltyFamily.HonorChanged(s, __state.Before, after, eligible: true, confirmedSpend: false));
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }
}

/// <summary>Only confirmed spending through TryRemoveFavor; ordinary honor decay and title loss are excluded.</summary>
[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.TryRemoveFavor))]
public static class RoyaltySpendHonorMetricPatch
{
    public static void Prefix(Pawn_RoyaltyTracker __instance, Pawn ___pawn, Faction faction, out (int Before, bool Eligible) __state)
    {
        __state = default;
        if (LiveMetrics.Family(RoyaltyFamily.Name) is null || ___pawn is null || faction is null)
            return;
        try { __state = (__instance.GetFavor(faction), ___pawn.IsFreeNonSlaveColonist && faction.def.HasRoyalTitles); }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }

    public static void Postfix(Pawn_RoyaltyTracker __instance, Faction faction, (int Before, bool Eligible) __state)
    {
        if (!__state.Eligible)
            return;
        try
        {
            var after = __instance.GetFavor(faction);
            GameFacts.Record(RoyaltyFamily.Name, s =>
                RoyaltyFamily.HonorChanged(s, __state.Before, after, eligible: true, confirmedSpend: true));
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }
}
