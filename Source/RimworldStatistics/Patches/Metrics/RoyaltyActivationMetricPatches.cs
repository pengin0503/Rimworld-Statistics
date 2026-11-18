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
    {
        __state = int.MinValue;
        try
        {
            if (__instance?.Faction?.def?.HasRoyalTitles != true || !LiveMetrics.CanCaptureEvent(RoyaltyFamily.Name))
                return;
            __state = __instance.LastUsedTick;
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }

    public static void Postfix(FactionPermit __instance, int __state)
    {
        if (__state == int.MinValue)
            return;
        try
        {
            if (__instance?.Faction?.def?.HasRoyalTitles != true || __instance.LastUsedTick == __state)
                return;
            var ownedByPlayer = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep
                .Any(p => GameFacts.IsMember(p)
                    && p.royalty?.AllFactionPermits?.Contains(__instance) == true);
            if (!ownedByPlayer)
                return;
            GameFacts.Record(RoyaltyFamily.Name, s =>
                RoyaltyFamily.PermitUsed(s, playerOwned: true, confirmed: true));
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
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

    public static int? Tick(Psycast? ability)
    {
        if (ability is not null && LastCastTick?.GetValue(ability) is int v)
            return v;
        return null;
    }
}

[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Psycast), nameof(Psycast.Activate), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
public static class RoyalPsycastLocalMetricPatch
{
    public static void Prefix(Psycast __instance, out int? __state)
    {
        __state = null;
        try
        {
            if (!GameFacts.IsMember(__instance?.pawn) || !LiveMetrics.CanCaptureEvent(RoyaltyFamily.Name))
                return;
            __state = RoyalPsycastFacts.Tick(__instance);
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }

    public static void Postfix(Psycast __instance, int? __state)
    {
        if (__state is not { } tick)
            return;
        try
        {
            if (!GameFacts.IsMember(__instance?.pawn)
                || RoyalPsycastFacts.Tick(__instance) is not { } after || after == tick)
                return;
            GameFacts.Record(RoyaltyFamily.Name, s => RoyaltyFamily.PsycastSucceeded(
                s, playerFreeColonist: true, confirmed: true));
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }
}

[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Psycast), nameof(Psycast.Activate), new[] { typeof(GlobalTargetInfo) })]
public static class RoyalPsycastWorldMetricPatch
{
    public static void Prefix(Psycast __instance, out int? __state)
    {
        __state = null;
        try
        {
            if (!GameFacts.IsMember(__instance?.pawn) || !LiveMetrics.CanCaptureEvent(RoyaltyFamily.Name))
                return;
            __state = RoyalPsycastFacts.Tick(__instance);
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }

    public static void Postfix(Psycast __instance, int? __state)
    {
        if (__state is not { } tick)
            return;
        try
        {
            if (!GameFacts.IsMember(__instance?.pawn)
                || RoyalPsycastFacts.Tick(__instance) is not { } after || after == tick)
                return;
            GameFacts.Record(RoyaltyFamily.Name, s => RoyaltyFamily.PsycastSucceeded(
                s, playerFreeColonist: true, confirmed: true));
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }
}
