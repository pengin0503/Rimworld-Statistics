using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Confirmation must be FactionPermit.Notify_Used, not a permit gizmo or
/// comms selection. Only permit instances owned by an existing free player
/// colonist count, not an NPC's empire permit or a cancelled target choice.
/// </summary>
[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(FactionPermit), nameof(FactionPermit.Notify_Used))]
public static class RoyalPermitUsedMetricPatch
{
    public static void Prefix(FactionPermit __instance, out int __state)
        => __state = !LiveMetrics.CanCaptureEvent(RoyaltyFamily.Name) ? int.MinValue : __instance.LastUsedTick;

    public static void Postfix(FactionPermit __instance, int __state)
    {
        if (__state == int.MinValue || __instance.Faction?.def?.HasRoyalTitles != true
            || __instance.LastUsedTick == __state)
            return;
        var ownedByPlayer = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep
            .Any(p => p != null && p.IsFreeNonSlaveColonist
                && p.royalty?.AllFactionPermits?.Contains(__instance) == true);
        GameFacts.Record(RoyaltyFamily.Name, s =>
            RoyaltyFamily.PermitUsed(s, playerOwned: ownedByPlayer, confirmed: true));
    }
}

/// <summary>
/// Activating an ability is not proof of a cast when target validation fails.
/// RimWorld.Ability.lastCastTick is written by confirmed casts in 1.7.4985:
/// a post-activation transition distinguishes the cast from queued UI commands.
/// </summary>
internal static class RoyalPsycastFacts
{
    private static readonly FieldInfo? LastCastTick = AccessTools.Field(typeof(Ability), "lastCastTick");

    public static int? Tick(Psycast ability)
    {
        if (LastCastTick?.GetValue(ability) is int v)
            return v;
        return null;
    }
}

[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Psycast), nameof(Psycast.Activate), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
public static class RoyalPsycastLocalMetricPatch
{
    public static void Prefix(Psycast __instance, out int? __state)
        => __state = LiveMetrics.CanCaptureEvent(RoyaltyFamily.Name) ? RoyalPsycastFacts.Tick(__instance) : null;

    public static void Postfix(Psycast __instance, int? __state)
    {
        var confirmed = __state is { } tick && RoyalPsycastFacts.Tick(__instance) is { } after && after != tick;
        GameFacts.Record(RoyaltyFamily.Name, s => RoyaltyFamily.PsycastSucceeded(
            s, playerFreeColonist: __instance.pawn?.IsFreeNonSlaveColonist == true, confirmed));
    }
}

[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Psycast), nameof(Psycast.Activate), new[] { typeof(GlobalTargetInfo) })]
public static class RoyalPsycastWorldMetricPatch
{
    public static void Prefix(Psycast __instance, out int? __state)
        => __state = LiveMetrics.CanCaptureEvent(RoyaltyFamily.Name) ? RoyalPsycastFacts.Tick(__instance) : null;

    public static void Postfix(Psycast __instance, int? __state)
    {
        var confirmed = __state is { } tick && RoyalPsycastFacts.Tick(__instance) is { } after && after != tick;
        GameFacts.Record(RoyaltyFamily.Name, s => RoyaltyFamily.PsycastSucceeded(
            s, playerFreeColonist: __instance.pawn?.IsFreeNonSlaveColonist == true, confirmed));
    }
}
