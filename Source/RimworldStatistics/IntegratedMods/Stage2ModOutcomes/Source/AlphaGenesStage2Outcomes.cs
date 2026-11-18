using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metrics for Alpha Genes (sarg.alphagenes, 1.7/Assemblies/AlphaGenes.dll):
/// 1. Gene absorptions: observes AlphaGenes.CompGeneSyphon.Apply(LocalTargetInfo, LocalTargetInfo)
///    and compares caster.genes.GenesListForReading before vs after so RejectInput failures
///    (target not downed/in bed or no eligible non-duplicate gene) record zero.
/// 2. Teratogenic mutations: observes AlphaGenes.CompTeratogenicHediff.Apply(LocalTargetInfo, LocalTargetInfo)
///    when a player caster converts existing Carcinoma hediffs into Props.hediff and removes the tumors.
/// </summary>
internal static class AlphaGenesStage2Outcomes
{
    private const string AssemblyName = "AlphaGenes";
    private const string GeneAbsorptionsUnit = "mod.sarg.alphagenes.gene_absorptions";
    private const string TeratogenicMutationsUnit = "mod.sarg.alphagenes.teratogenic_mutations";

    private static PropertyInfo teratogenicPropsProperty = null!;
    private static FieldInfo targetOtherField = null!;
    private static FieldInfo mutationHediffField = null!;

    private sealed record SyphonSnapshot(Pawn Caster, Dictionary<GeneDef, int> BeforeCounts);

    private sealed record TeratogenicSnapshot(Pawn Target, HediffDef MutationDef, int CarcinomaCountBefore);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            AlphaGenesStage2Rules.OwnerPackageId,
            "gene_absorptions",
            "RWS_AlphaGenesGeneAbsorptions",
            "RWS_AlphaGenesGeneAbsorptionsDesc",
            MetricCategory.Medicine,
            InstallGeneAbsorptions,
            breakdown: true,
            dlc: AcquisitionUnits.Biotech);

        yield return OutcomeMetric.Count(
            AlphaGenesStage2Rules.OwnerPackageId,
            "teratogenic_mutations",
            "RWS_AlphaGenesTeratogenicMutations",
            "RWS_AlphaGenesTeratogenicMutationsDesc",
            MetricCategory.Medicine,
            InstallTeratogenicMutations,
            breakdown: true,
            dlc: AcquisitionUnits.Biotech);
    }

    private static void InstallGeneAbsorptions(Harmony harmony)
    {
        var syphonType = OutcomeMetric.Bind("AlphaGenes.CompGeneSyphon", AssemblyName);
        if (!typeof(CompAbilityEffect).IsAssignableFrom(syphonType))
            throw new InvalidOperationException("AlphaGenes.CompGeneSyphon no longer derives from CompAbilityEffect");
        var apply = OutcomeMetric.Method(syphonType, "Apply", typeof(void), typeof(LocalTargetInfo), typeof(LocalTargetInfo));
        OutcomeMetric.Patch(harmony, apply, typeof(AlphaGenesStage2Outcomes), nameof(BeforeSyphon), nameof(AfterSyphon));
    }

    private static void BeforeSyphon(CompAbilityEffect __instance, out SyphonSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(GeneAbsorptionsUnit) ||
            __instance?.parent?.pawn is not { Dead: false, Destroyed: false, genes: { } genes } caster ||
            !AlphaGenesStage2Rules.IsEligibleSyphonCasterBefore(
                isPlayerCaster: GameFacts.IsPlayer(caster),
                isCasterAlive: true,
                hasGeneTracker: true))
            return;

        try
        {
            var before = new Dictionary<GeneDef, int>();
            var list = genes.GenesListForReading;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i]?.def is { } def)
                    before[def] = before.TryGetValue(def, out var count) ? count + 1 : 1;
            }
            __state = new SyphonSnapshot(caster, before);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(GeneAbsorptionsUnit, error);
        }
    }

    private static void AfterSyphon(bool __runOriginal, SyphonSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(GeneAbsorptionsUnit))
            return;

        try
        {
            if (__state.Caster is not { Dead: false, Destroyed: false, genes: { } genes })
                return;

            var after = new Dictionary<GeneDef, int>();
            var list = genes.GenesListForReading;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i]?.def is { } def)
                    after[def] = after.TryGetValue(def, out var count) ? count + 1 : 1;
            }

            foreach (var (def, afterCount) in after)
            {
                __state.BeforeCounts.TryGetValue(def, out var beforeCount);
                var delta = AlphaGenesStage2Rules.ComputeNewlyAddedGeneCount(beforeCount, afterCount);
                if (delta > 0)
                    OutcomeMetric.Record(GeneAbsorptionsUnit, def, breakdown: true, amount: delta);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(GeneAbsorptionsUnit, error);
        }
    }

    private static void InstallTeratogenicMutations(Harmony harmony)
    {
        var compType = OutcomeMetric.Bind("AlphaGenes.CompTeratogenicHediff", AssemblyName);
        var propsType = OutcomeMetric.Bind("AlphaGenes.CompProperties_TeratogenicHediff", AssemblyName);
        if (!typeof(CompAbilityEffect).IsAssignableFrom(compType))
            throw new InvalidOperationException("AlphaGenes.CompTeratogenicHediff no longer derives from CompAbilityEffect");

        teratogenicPropsProperty = AccessTools.Property(compType, "Props") is { GetMethod: not null } prop && prop.PropertyType == propsType
            ? prop
            : throw new InvalidOperationException("AlphaGenes.CompTeratogenicHediff.Props signature changed");
        targetOtherField = AccessTools.DeclaredField(propsType, "targetOther") is { } tf && tf.FieldType == typeof(bool)
            ? tf
            : throw new InvalidOperationException("AlphaGenes.CompProperties_TeratogenicHediff.targetOther signature changed");
        mutationHediffField = AccessTools.DeclaredField(propsType, "hediff") is { } hf && hf.FieldType == typeof(HediffDef)
            ? hf
            : throw new InvalidOperationException("AlphaGenes.CompProperties_TeratogenicHediff.hediff signature changed");

        var apply = OutcomeMetric.Method(compType, "Apply", typeof(void), typeof(LocalTargetInfo), typeof(LocalTargetInfo));
        OutcomeMetric.Patch(harmony, apply, typeof(AlphaGenesStage2Outcomes), nameof(BeforeTeratogenic), nameof(AfterTeratogenic));
    }

    private static int CountCarcinomas(Pawn pawn)
    {
        var hediffs = pawn.health?.hediffSet?.hediffs;
        if (hediffs is null)
            return 0;
        var count = 0;
        for (var i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i]?.def == HediffDefOf.Carcinoma)
                count++;
        }
        return count;
    }

    private static void BeforeTeratogenic(CompAbilityEffect __instance, LocalTargetInfo __0, out TeratogenicSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(TeratogenicMutationsUnit) ||
            __instance?.parent?.pawn is not { Dead: false, Destroyed: false } caster ||
            !GameFacts.IsPlayer(caster))
            return;

        try
        {
            if (teratogenicPropsProperty.GetValue(__instance) is not { } props ||
                targetOtherField.GetValue(props) is not bool targetOther ||
                mutationHediffField.GetValue(props) is not HediffDef mutationDef)
                return;

            var targetPawn = targetOther ? __0.Pawn : caster;
            if (targetPawn is not { Dead: false, Destroyed: false })
                return;

            var before = CountCarcinomas(targetPawn);
            if (before > 0)
                __state = new TeratogenicSnapshot(targetPawn, mutationDef, before);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(TeratogenicMutationsUnit, error);
        }
    }

    private static void AfterTeratogenic(bool __runOriginal, TeratogenicSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(TeratogenicMutationsUnit))
            return;

        try
        {
            var target = __state.Target;
            if (target is not { Dead: false, Destroyed: false })
                return;

            var after = CountCarcinomas(target);
            var hasMutation = target.health?.hediffSet?.GetFirstHediffOfDef(__state.MutationDef) is not null;
            if (AlphaGenesStage2Rules.ShouldRecordTeratogenicMutation(
                    isPlayerCaster: true,
                    isTargetAlive: true,
                    carcinomaCountBefore: __state.CarcinomaCountBefore,
                    carcinomaCountAfter: after,
                    hasMutationHediffAfter: hasMutation))
            {
                OutcomeMetric.Record(TeratogenicMutationsUnit, __state.MutationDef, breakdown: true);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(TeratogenicMutationsUnit, error);
        }
    }
}
