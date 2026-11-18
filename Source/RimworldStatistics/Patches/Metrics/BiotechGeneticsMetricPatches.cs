using System.Globalization;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

internal static class BiotechGeneticsProbe
{
    public static bool CanObserve()
        => UnityData.IsInMainThread
            && Scribe.mode == LoadSaveMode.Inactive
            && LiveMetrics.IsRecordingUnit(BiotechGeneticsFamily.Name)
            && ModsConfig.BiotechActive;

    public static bool IsColonyPawn(Pawn? pawn)
        => pawn is { Dead: false, Destroyed: false }
            && (GameFacts.IsPlayerFaction(pawn.Faction)
                || GameFacts.IsPrisonerOfColonySafe(pawn)
                || (pawn.IsSlave && GameFacts.IsPlayerFaction(pawn.SlaveFaction)));
}

/// <summary>
/// Records growth vat child-growth completions when Building_GrowthVat.FinishPawn drops a living pawn that reached the vat's
/// eject age. "Cancel vatgrowth" ejects a younger child through the same FinishPawn and is not counted (#391).
/// </summary>
[MetricAdapter(BiotechGeneticsFamily.Name)]
[HarmonyPatch(typeof(Building_GrowthVat), "FinishPawn")]
public static class BiotechGrowthVatPawnMetricPatch
{
    public static void Prefix(Building_GrowthVat __instance, out Pawn? __state)
    {
        __state = null;
        if (!BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (__instance is { Destroyed: false }
                && GameFacts.IsPlayerFaction(__instance.Faction)
                && __instance.SelectedPawn is { } pawn
                && __instance.innerContainer?.Contains(pawn) == true
                && pawn.ageTracker is { } age
                && BiotechGeneticsFamily.IsGrowthVatCompletionAge(age.AgeBiologicalYearsFloat)
                && LiveMetrics.CanCaptureEvent(BiotechGeneticsFamily.Name))
            {
                __state = pawn;
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }

    public static void Postfix(Building_GrowthVat __instance, Pawn? __state)
    {
        if (__state is null || !BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            var releasedAlive = __instance?.innerContainer?.Contains(__state) == false
                && __state is { Dead: false, Destroyed: false, Spawned: true };
            if (releasedAlive)
                GameFacts.Record(BiotechGeneticsFamily.Name, s =>
                    BiotechGeneticsFamily.GrowthVatCompleted(s, playerVat: true, livingResult: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }
}

/// <summary>
/// Records growth vat embryo gestation completions when FinishEmbryo produces a living newborn pawn on the map.
/// </summary>
[MetricAdapter(BiotechGeneticsFamily.Name)]
[HarmonyPatch(typeof(Building_GrowthVat), "FinishEmbryo")]
public static class BiotechGrowthVatEmbryoMetricPatch
{
    public static void Prefix(Building_GrowthVat __instance, HumanEmbryo ___selectedEmbryo,
        out (bool Tracked, Map? Map, int SpawnedHumanlikesBefore) __state)
    {
        __state = default;
        if (!BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (__instance is { Destroyed: false, Map: { } map }
                && GameFacts.IsPlayerFaction(__instance.Faction)
                && ___selectedEmbryo is not null
                && __instance.innerContainer?.Contains(___selectedEmbryo) == true
                && LiveMetrics.CanCaptureEvent(BiotechGeneticsFamily.Name))
            {
                __state = (true, map, map.mapPawns?.AllHumanlikeSpawned?.Count ?? 0);
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }

    public static void Postfix(HumanEmbryo ___selectedEmbryo,
        (bool Tracked, Map? Map, int SpawnedHumanlikesBefore) __state)
    {
        if (!__state.Tracked || !BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            var afterCount = __state.Map?.mapPawns?.AllHumanlikeSpawned?.Count ?? 0;
            if (___selectedEmbryo is null && afterCount > __state.SpawnedHumanlikesBefore)
                GameFacts.Record(BiotechGeneticsFamily.Name, s =>
                    BiotechGeneticsFamily.GrowthVatCompleted(s, playerVat: true, livingResult: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }
}

/// <summary>
/// Records completed gene extractions on a player Building_GeneExtractor that spawn a Genepack on the map.
/// </summary>
[MetricAdapter(BiotechGeneticsFamily.Name)]
[HarmonyPatch(typeof(Building_GeneExtractor), "Finish")]
public static class BiotechGeneExtractorFinishMetricPatch
{
    public static void Prefix(Building_GeneExtractor __instance,
        out (bool Tracked, Map? Map, int GenepacksBefore) __state)
    {
        __state = default;
        if (!BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (__instance is { Destroyed: false, Map: { } map }
                && __instance.innerContainer?.FirstOrDefault() is Pawn
                && GameFacts.IsPlayerFaction(__instance.Faction)
                && LiveMetrics.CanCaptureEvent(BiotechGeneticsFamily.Name))
            {
                var before = map.listerThings?.ThingsOfDef(ThingDefOf.Genepack)?.Count ?? 0;
                __state = (true, map, before);
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }

    public static void Postfix((bool Tracked, Map? Map, int GenepacksBefore) __state)
    {
        if (!__state.Tracked || !BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            var after = __state.Map?.listerThings?.ThingsOfDef(ThingDefOf.Genepack)?.Count ?? 0;
            if (after > __state.GenepacksBefore)
                GameFacts.Record(BiotechGeneticsFamily.Name, s =>
                    BiotechGeneticsFamily.GeneExtracted(s, playerExtractor: true, genepackProduced: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }
}

/// <summary>
/// Records completed xenogerm recombinations on a player Building_GeneAssembler.
/// </summary>
[MetricAdapter(BiotechGeneticsFamily.Name)]
[HarmonyPatch(typeof(Building_GeneAssembler), nameof(Building_GeneAssembler.Finish))]
public static class BiotechGeneAssemblerFinishMetricPatch
{
    public static void Prefix(Building_GeneAssembler __instance,
        out (bool Tracked, Map? Map, int XenogermsBefore) __state)
    {
        __state = default;
        if (!BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (__instance is { Destroyed: false, Working: true, Map: { } map }
                && GameFacts.IsPlayerFaction(__instance.Faction)
                && LiveMetrics.CanCaptureEvent(BiotechGeneticsFamily.Name))
            {
                var before = map.listerThings?.ThingsOfDef(ThingDefOf.Xenogerm)?.Count ?? 0;
                __state = (true, map, before);
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }

    public static void Postfix(Building_GeneAssembler __instance,
        (bool Tracked, Map? Map, int XenogermsBefore) __state)
    {
        if (!__state.Tracked || !BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            var after = __state.Map?.listerThings?.ThingsOfDef(ThingDefOf.Xenogerm)?.Count ?? 0;
            if (__instance is { Working: false } && after > __state.XenogermsBefore)
                GameFacts.Record(BiotechGeneticsFamily.Name, s =>
                    BiotechGeneticsFamily.XenogermAssembled(s, playerAssembler: true, xenogermProduced: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }
}

/// <summary>
/// Records xenogerm item implantations onto colony pawns (colonists, prisoners, or slaves).
/// </summary>
[MetricAdapter(BiotechGeneticsFamily.Name)]
[HarmonyPatch(typeof(GeneUtility), nameof(GeneUtility.ImplantXenogermItem))]
public static class BiotechImplantXenogermItemMetricPatch
{
    public static void Prefix(Pawn pawn, Xenogerm xenogerm, out string? __state)
    {
        __state = null;
        if (!BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (BiotechGeneticsProbe.IsColonyPawn(pawn)
                && xenogerm is not null
                && pawn.genes is not null
                && LiveMetrics.CanCaptureEvent(BiotechGeneticsFamily.Name))
            {
                __state = "item:" + GameFacts.Id(pawn) + ":" + GameFacts.Id(xenogerm) + ":"
                    + GameFacts.Tick.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }

    public static void Postfix(Pawn pawn, string? __state)
    {
        if (__state is null || !BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (pawn?.genes is not null)
                GameFacts.Record(BiotechGeneticsFamily.Name, s =>
                    BiotechGeneticsFamily.XenogermImplanted(s, __state, colonyAffiliated: true, implanted: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }
}

/// <summary>
/// Records direct xenogerm reimplantations involving a colony caster or recipient.
/// </summary>
[MetricAdapter(BiotechGeneticsFamily.Name)]
[HarmonyPatch(typeof(GeneUtility), nameof(GeneUtility.ReimplantXenogerm))]
public static class BiotechReimplantXenogermMetricPatch
{
    public static void Prefix(Pawn caster, Pawn recipient, out string? __state)
    {
        __state = null;
        if (!BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (caster?.genes is not null && recipient?.genes is not null
                && (BiotechGeneticsProbe.IsColonyPawn(caster) || BiotechGeneticsProbe.IsColonyPawn(recipient))
                && LiveMetrics.CanCaptureEvent(BiotechGeneticsFamily.Name))
            {
                __state = "reimplant:" + GameFacts.Id(caster) + ":" + GameFacts.Id(recipient) + ":"
                    + GameFacts.Tick.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }

    public static void Postfix(Pawn recipient, string? __state)
    {
        if (__state is null || !BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (recipient?.genes is not null)
                GameFacts.Record(BiotechGeneticsFamily.Name, s =>
                    BiotechGeneticsFamily.XenogermImplanted(s, __state, colonyAffiliated: true, implanted: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }
}

/// <summary>
/// Captures the exact game tick when a mechanitor boss type transitions from undefeated to defeated.
/// </summary>
[MetricAdapter(BiotechGeneticsFamily.Name)]
[HarmonyPatch(typeof(GameComponent_Bossgroup), nameof(GameComponent_Bossgroup.Notify_PawnKilled))]
public static class BiotechBossDefeatedMetricPatch
{
    public static void Prefix(GameComponent_Bossgroup __instance, Pawn pawn, out BossDef? __state)
    {
        __state = null;
        if (!BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (__instance is null || pawn?.kindDef is not { } kind
                || !LiveMetrics.CanCaptureEvent(BiotechGeneticsFamily.Name))
                return;
            var bossDef = __instance.AllBosses?.FirstOrDefault(b => b?.kindDef == kind);
            if (bossDef is not null && !__instance.IsDefeated(bossDef))
                __state = bossDef;
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }

    public static void Postfix(GameComponent_Bossgroup __instance, BossDef? __state)
    {
        if (__state is null || !BiotechGeneticsProbe.CanObserve())
            return;
        try
        {
            if (__instance is not null && __instance.IsDefeated(__state))
            {
                var qualified = OptionalTransitionRecording.QualifiedDefKey(__state.modContentPack?.PackageId, __state.defName);
                if (!string.IsNullOrWhiteSpace(qualified))
                {
                    var tick = GameFacts.Tick;
                    GameFacts.Record(BiotechGeneticsFamily.Name, s =>
                        BiotechGeneticsFamily.BossFirstDefeated(s, qualified!, tick));
                }
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BiotechGeneticsFamily.Name, e);
        }
    }
}
