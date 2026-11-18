using System.Globalization;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.Patches.Metrics;

internal static class AnomalyHoldingProbe
{
    public static bool CanObserveAnomalyEvent()
        => UnityData.IsInMainThread
            && Scribe.mode == LoadSaveMode.Inactive
            && ModsConfig.AnomalyActive;

    public static IEnumerable<string> CurrentlyHeldPawnIds()
    {
        if (!ModsConfig.AnomalyActive || Find.Maps is not { } maps || GameFacts.PlayerFaction is not { } player)
            yield break;
        foreach (var map in maps)
        {
            if (map?.listerBuildings?.allBuildingsColonist is not { } buildings)
                continue;
            foreach (var building in buildings)
                if (building is Building_HoldingPlatform platform
                    && platform.Faction == player
                    && platform.HeldPawn is { Dead: false, Destroyed: false } pawn)
                    yield return GameFacts.Id(pawn);
        }
    }
}

/// <summary>
/// Completed player psychic rituals (including modded PsychicRitualDefs registered on the vanilla
/// Anomaly ritual graph). Enemy cultist siege rituals have playerRitual == false and never count.
/// </summary>
[MetricAdapter(AnomalyFamily.Name)]
[HarmonyPatch(typeof(LordToil_PsychicRitual), "RitualCompleted")]
public static class AnomalyPsychicRitualCompletedMetricPatch
{
    public static void Postfix(LordToil_PsychicRitual __instance)
    {
        if (!AnomalyHoldingProbe.CanObserveAnomalyEvent())
            return;
        try
        {
            if (__instance?.data is not PsychicRitualLordToilData { playerRitual: true } data
                || data.psychicRitual is not { canceled: false } ritual)
                return;
            if (!LiveMetrics.CanCaptureEvent(AnomalyFamily.Name))
                return;
            var lordId = __instance.lord?.GetUniqueLoadID() ?? "Lord_unknown";
            var eventId = lordId + "/" + data.iteration.ToString(CultureInfo.InvariantCulture)
                + "/" + GameFacts.Tick.ToString(CultureInfo.InvariantCulture);
            var def = ritual.def;
            var qualifiedDef = def is null
                ? null
                : OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName);
            GameFacts.Record(AnomalyFamily.Name, s =>
                AnomalyFamily.PsychicRitualCompleted(s, eventId, qualifiedDef, playerRitual: true, completed: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(AnomalyFamily.Name, e);
        }
    }
}

/// <summary>
/// Records living entities breaking free from a player-owned Building_HoldingPlatform.
/// Multi-platform room escapes recursively call Escape(false) on each joining platform,
/// so each escaping entity passes through Escape once while still held in Prefix.
/// </summary>
[MetricAdapter(AnomalyFamily.Name)]
[HarmonyPatch(typeof(CompHoldingPlatformTarget), nameof(CompHoldingPlatformTarget.Escape))]
public static class AnomalyEntityEscapeMetricPatch
{
    public static void Prefix(CompHoldingPlatformTarget __instance, out string? __state)
    {
        __state = null;
        if (!AnomalyHoldingProbe.CanObserveAnomalyEvent())
            return;
        try
        {
            if (__instance?.parent is Pawn { Dead: false, Destroyed: false } pawn
                && __instance.HeldPlatform is { } platform
                && GameFacts.IsPlayerFaction(platform.Faction)
                && LiveMetrics.CanCaptureEvent(AnomalyFamily.Name))
                __state = GameFacts.Id(pawn);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(AnomalyFamily.Name, e);
        }
    }

    public static void Postfix(CompHoldingPlatformTarget __instance, string? __state)
    {
        if (__state is null || !AnomalyHoldingProbe.CanObserveAnomalyEvent())
            return;
        try
        {
            var escaped = __instance is { isEscaping: true, HeldPlatform: null }
                && __instance.parent is Pawn { Dead: false, Destroyed: false };
            if (escaped)
                GameFacts.Record(AnomalyFamily.Name, s =>
                    AnomalyFamily.EntityEscaped(s, __state, playerPlatform: true, escapedFromPlatform: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(AnomalyFamily.Name, e);
        }
    }
}

/// <summary>
/// Records entities newly secured on a player-owned Building_HoldingPlatform.
/// Non-pawn items such as RevenantSpine spawn a Pawn and invoke Notify_HeldOnPlatform
/// on the new Pawn before destroying the spine, so filtering to living non-destroyed Pawns
/// and deduplicating by Pawn ID counts conversions once and ignores platform transfers.
/// </summary>
[MetricAdapter(AnomalyFamily.Name)]
[HarmonyPatch(typeof(CompHoldingPlatformTarget), nameof(CompHoldingPlatformTarget.Notify_HeldOnPlatform))]
public static class AnomalyEntityContainedMetricPatch
{
    public static void Postfix(CompHoldingPlatformTarget __instance)
    {
        if (!AnomalyHoldingProbe.CanObserveAnomalyEvent())
            return;
        try
        {
            if (__instance?.parent is not Pawn { Dead: false, Destroyed: false } pawn
                || __instance.HeldPlatform is not { } platform
                || !GameFacts.IsPlayerFaction(platform.Faction)
                || !LiveMetrics.CanCaptureEvent(AnomalyFamily.Name))
                return;
            var pawnId = GameFacts.Id(pawn);
            GameFacts.Record(AnomalyFamily.Name, s =>
                AnomalyFamily.EntityContained(s, pawnId, playerPlatform: true, livingEntityOnPlatform: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(AnomalyFamily.Name, e);
        }
    }
}

/// <summary>
/// Records PitGate post-Dreadmeld collapse completions when Collapse() destroys the gate.
/// </summary>
[MetricAdapter(AnomalyFamily.Name)]
[HarmonyPatch(typeof(PitGate), "Collapse")]
public static class AnomalyPitGateCollapseMetricPatch
{
    public static void Prefix(PitGate __instance, out string? __state)
    {
        __state = null;
        if (!AnomalyHoldingProbe.CanObserveAnomalyEvent())
            return;
        try
        {
            if (__instance is { Destroyed: false, Spawned: true, IsCollapsing: true, Map: not null }
                && LiveMetrics.CanCaptureEvent(AnomalyFamily.Name))
                __state = GameFacts.Id(__instance);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(AnomalyFamily.Name, e);
        }
    }

    public static void Postfix(PitGate __instance, string? __state)
    {
        if (__state is null || !AnomalyHoldingProbe.CanObserveAnomalyEvent())
            return;
        try
        {
            if (__instance is { Destroyed: true })
                GameFacts.Record(AnomalyFamily.Name, s =>
                    AnomalyFamily.PitGateCollapsed(s, __state, confirmedCollapse: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(AnomalyFamily.Name, e);
        }
    }
}
