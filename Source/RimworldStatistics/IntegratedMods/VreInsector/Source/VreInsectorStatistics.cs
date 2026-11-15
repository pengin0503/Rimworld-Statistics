using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VreInsector;

/// <summary>
/// Vanilla Races Expanded - Insector: tracks committed metapod geneline
/// evolutions/changes and reversions to baseline Insector via MetapodHediff.Complete().
/// Dead-pawn direct edits and chestburst births (already counted by vanilla births) are excluded.
/// </summary>
internal static class VreInsectorStatistics
{
    private const string Owner = "vanillaracesexpanded.insector";
    private const string AssemblyName = "VanillaRacesExpanded-Insector";
    private const string EvolutionUnit = "mod.vanillaracesexpanded.insector.geneline_evolutions";
    private const string ReversionUnit = "mod.vanillaracesexpanded.insector.geneline_reversions";

    private static Type? evolutionGeneType;
    private static Type? genelineGeneDefType;
    private static FieldInfo? curGenelineField;
    private static FieldInfo? geneGenelineField;
    private static bool patched;

    private sealed record MetapodBefore(
        Pawn Pawn,
        HediffWithComps Hediff,
        object? TargetGeneline,
        object? BeforeGeneline,
        HashSet<GeneDef> BeforeGenes);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(Owner, "geneline_evolutions",
            "RWS_VreInsector_GenelineEvolutions", "RWS_VreInsector_GenelineEvolutionsDesc",
            MetricCategory.Biotech, Install, breakdown: false, dlc: AcquisitionUnits.Biotech),
        OutcomeMetric.Count(Owner, "geneline_reversions",
            "RWS_VreInsector_GenelineReversions", "RWS_VreInsector_GenelineReversionsDesc",
            MetricCategory.Biotech, Install, breakdown: false, dlc: AcquisitionUnits.Biotech),
    ];

    private static void Install(Harmony harmony)
    {
        if (patched)
            return;

        var hediffType = OutcomeMetric.Bind("VanillaRacesExpandedInsector.MetapodHediff", AssemblyName);
        var genelineType = OutcomeMetric.Bind("VanillaRacesExpandedInsector.Geneline", AssemblyName);
        var evoType = OutcomeMetric.Bind("VanillaRacesExpandedInsector.Gene_GenelineEvolution", AssemblyName);
        var geneDefType = OutcomeMetric.Bind("VanillaRacesExpandedInsector.GenelineGeneDef", AssemblyName);

        if (!typeof(HediffWithComps).IsAssignableFrom(hediffType) ||
            !typeof(Gene).IsAssignableFrom(evoType) ||
            !typeof(GeneDef).IsAssignableFrom(geneDefType))
            throw new InvalidOperationException("Insector metapod type hierarchy changed");

        var curGeneline = AccessTools.DeclaredField(hediffType, "curGeneline");
        var geneGeneline = AccessTools.DeclaredField(evoType, "geneline");
        if (curGeneline?.FieldType != genelineType || geneGeneline?.FieldType != genelineType)
            throw new InvalidOperationException("Insector geneline reference fields changed");

        var complete = OutcomeMetric.Method(hediffType, "Complete", typeof(void));

        evolutionGeneType = evoType;
        genelineGeneDefType = geneDefType;
        curGenelineField = curGeneline;
        geneGenelineField = geneGeneline;
        try
        {
            OutcomeMetric.Patch(harmony, complete, typeof(VreInsectorStatistics),
                nameof(BeforeComplete), nameof(AfterComplete));
            patched = true;
        }
        catch
        {
            evolutionGeneType = null;
            genelineGeneDefType = null;
            curGenelineField = null;
            geneGenelineField = null;
            throw;
        }
    }

    private static bool ObservingAny()
        => OutcomeMetric.Observing(EvolutionUnit) || OutcomeMetric.Observing(ReversionUnit);

    private static Gene? FindEvolutionGene(Pawn pawn)
    {
        if (evolutionGeneType is null || pawn.genes is null)
            return null;
        var genes = pawn.genes.GenesListForReading;
        for (int i = 0; i < genes.Count; i++)
        {
            var gene = genes[i];
            if (gene is not null && evolutionGeneType.IsInstanceOfType(gene))
                return gene;
        }
        return null;
    }

    private static HashSet<GeneDef> CollectGenelineGenes(Pawn pawn)
    {
        var result = new HashSet<GeneDef>();
        if (genelineGeneDefType is null || pawn.genes is null)
            return result;
        var genes = pawn.genes.GenesListForReading;
        for (int i = 0; i < genes.Count; i++)
        {
            var def = genes[i]?.def;
            if (def is not null && genelineGeneDefType.IsInstanceOfType(def))
                result.Add(def);
        }
        return result;
    }

    private static void BeforeComplete(object __instance, out MetapodBefore? __state)
    {
        __state = null;
        if (!ObservingAny())
            return;
        try
        {
            if (__instance is not HediffWithComps { pawn: { Dead: false, genes: not null } pawn } hediff ||
                !GameFacts.IsPlayer(pawn) ||
                pawn.health?.hediffSet?.hediffs.Contains(hediff) != true)
                return;

            var targetGeneline = curGenelineField?.GetValue(__instance);
            var evoGene = FindEvolutionGene(pawn);
            var beforeGeneline = evoGene is not null ? geneGenelineField?.GetValue(evoGene) : null;
            var beforeGenes = CollectGenelineGenes(pawn);
            __state = new MetapodBefore(pawn, hediff, targetGeneline, beforeGeneline, beforeGenes);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(EvolutionUnit, e);
            LiveMetrics.Fail(ReversionUnit, e);
        }
    }

    private static void AfterComplete(bool __runOriginal, MetapodBefore? __state)
    {
        if (!__runOriginal || __state is null || !ObservingAny())
            return;
        try
        {
            var pawn = __state.Pawn;
            bool livePlayer = !pawn.Dead && GameFacts.IsPlayer(pawn) && pawn.genes is not null;
            bool removed = pawn.health?.hediffSet is { } hs && !hs.hediffs.Contains(__state.Hediff);
            if (!livePlayer || !removed)
                return;

            var evoAfter = FindEvolutionGene(pawn);
            var afterGeneline = evoAfter is not null ? geneGenelineField?.GetValue(evoAfter) : null;
            var afterGenes = CollectGenelineGenes(pawn);

            var outcome = VreInsectorRules.ClassifyCompletion(
                livePlayerPawn: true,
                metapodRemoved: true,
                hasTargetGeneline: __state.TargetGeneline is not null,
                hasEvolutionGeneAfter: evoAfter is not null,
                targetGenelineMatchedAfter: ReferenceEquals(afterGeneline, __state.TargetGeneline),
                genelineChanged: !ReferenceEquals(__state.BeforeGeneline, afterGeneline),
                beforeGenelineGeneCount: __state.BeforeGenes.Count,
                afterGenelineGeneCount: afterGenes.Count,
                genelineGenesChanged: !__state.BeforeGenes.SetEquals(afterGenes));

            if (outcome == MetapodOutcome.EvolutionOrChange && OutcomeMetric.Observing(EvolutionUnit))
                OutcomeMetric.Record(EvolutionUnit);
            else if (outcome == MetapodOutcome.Reversion && OutcomeMetric.Observing(ReversionUnit))
                OutcomeMetric.Record(ReversionUnit);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(EvolutionUnit, e);
            LiveMetrics.Fail(ReversionUnit, e);
        }
    }
}
