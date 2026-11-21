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
        if (!LiveMetrics.IsRecordingUnit(RoyaltyFamily.Name)
            || !UnityData.IsInMainThread
            || Scribe.mode != LoadSaveMode.Inactive)
            return;
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
            var ownedByPlayer = false;
            var colonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep;
            for (var i = 0; i < colonists.Count; i++)
            {
                var p = colonists[i];
                if (GameFacts.IsMember(p) && p.royalty?.AllFactionPermits?.Contains(__instance) == true)
                {
                    ownedByPlayer = true;
                    break;
                }
            }
            if (!ownedByPlayer)
                return;
            var permitDefKey = OptionalTransitionRecording.QualifiedDefKey(
                __instance.Permit?.modContentPack?.PackageId,
                __instance.Permit?.defName);
            GameFacts.Record(RoyaltyFamily.Name, s =>
                RoyaltyFamily.PermitUsed(s, playerOwned: true, confirmed: true, permitDefKey));
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
    private static readonly AccessTools.FieldRef<Ability, int>? LastCastTickRef = CreateLastCastTickRef();

    private static AccessTools.FieldRef<Ability, int>? CreateLastCastTickRef()
    {
        try
        {
            var field = AccessTools.Field(typeof(Ability), "lastCastTick");
            return field?.FieldType == typeof(int)
                ? AccessTools.FieldRefAccess<Ability, int>(field)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static int? Tick(Psycast? ability) =>
        ability is not null && LastCastTickRef is not null ? LastCastTickRef(ability) : null;
}

[MetricAdapter(RoyaltyFamily.Name)]
[HarmonyPatch(typeof(Psycast), nameof(Psycast.Activate), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
public static class RoyalPsycastLocalMetricPatch
{
    public static void Prefix(Psycast __instance, out int? __state)
    {
        __state = null;
        if (!LiveMetrics.IsRecordingUnit(RoyaltyFamily.Name)
            || !UnityData.IsInMainThread
            || Scribe.mode != LoadSaveMode.Inactive)
            return;
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
            var abilityDefKey = OptionalTransitionRecording.QualifiedDefKey(
                __instance.def?.modContentPack?.PackageId,
                __instance.def?.defName);
            GameFacts.Record(RoyaltyFamily.Name, s => RoyaltyFamily.PsycastSucceeded(
                s, playerFreeColonist: true, confirmed: true, abilityDefKey));
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
        if (!LiveMetrics.IsRecordingUnit(RoyaltyFamily.Name)
            || !UnityData.IsInMainThread
            || Scribe.mode != LoadSaveMode.Inactive)
            return;
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
            var abilityDefKey = OptionalTransitionRecording.QualifiedDefKey(
                __instance.def?.modContentPack?.PackageId,
                __instance.def?.defName);
            GameFacts.Record(RoyaltyFamily.Name, s => RoyaltyFamily.PsycastSucceeded(
                s, playerFreeColonist: true, confirmed: true, abilityDefKey));
        }
        catch (Exception e) { LiveMetrics.Fail(RoyaltyFamily.Name, e); }
    }
}
