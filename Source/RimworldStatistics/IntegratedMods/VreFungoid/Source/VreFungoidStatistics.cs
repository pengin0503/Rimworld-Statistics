using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VreFungoid;

/// <summary>
/// Vanilla Races Expanded - Fungoid: counts completed fungal gene infection
/// conversions on colonists, slaves, and colony prisoners. Conversion-result
/// breakdown is omitted because upstream discards source XenotypeDef and always
/// sets Baseliner plus a localized/custom string name.
/// </summary>
internal static class VreFungoidStatistics
{
    private const string Owner = "vanillaracesexpanded.fungoid";
    private const string AssemblyName = "VanillaRacesExpanded-Fungoid";
    private const string Unit = "mod.vanillaracesexpanded.fungoid.infection_conversions";

    private static FieldInfo? geneInfectedField;

    private sealed record InfectionBefore(Pawn Pawn, HediffWithComps Hediff);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(Owner, "infection_conversions",
            "RWS_VreFungoid_InfectionConversions", "RWS_VreFungoid_InfectionConversionsDesc",
            MetricCategory.Biotech, Install, breakdown: false, dlc: AcquisitionUnits.Biotech),
    ];

    private static void Install(Harmony harmony)
    {
        var comp = OutcomeMetric.Bind("VanillaRacesExpandedFungoid.HediffComp_GeneInfection", AssemblyName);
        if (!typeof(HediffComp).IsAssignableFrom(comp))
            throw new InvalidOperationException("Fungoid HediffComp_GeneInfection base type changed");

        var defOf = OutcomeMetric.Bind("VanillaRacesExpandedFungoid.InternalDefOf", AssemblyName);
        var infected = AccessTools.DeclaredField(defOf, "VRE_GeneInfected");
        if (infected is null || !infected.IsStatic || infected.FieldType != typeof(HediffDef))
            throw new InvalidOperationException("Fungoid InternalDefOf.VRE_GeneInfected changed");

        var tick = OutcomeMetric.Method(comp, "CompPostTickInterval", typeof(void),
            typeof(float).MakeByRefType(), typeof(int));

        geneInfectedField = infected;
        try
        {
            OutcomeMetric.Patch(harmony, tick, typeof(VreFungoidStatistics),
                nameof(BeforeTick), nameof(AfterTick));
        }
        catch
        {
            geneInfectedField = null;
            throw;
        }
    }

    private static bool IsColonyAffected(Pawn pawn)
        => GameFacts.IsPlayer(pawn) || pawn.IsPrisonerOfColony;

    private static void BeforeTick(object __instance, out InfectionBefore? __state)
    {
        __state = null;
        if (__instance is not HediffComp { parent: { Severity: > 0.99f } hediff } ||
            !OutcomeMetric.Observing(Unit))
            return;
        try
        {
            if (hediff.pawn is not { Dead: false, genes: not null } pawn ||
                !IsColonyAffected(pawn) ||
                pawn.health?.hediffSet?.hediffs.Contains(hediff) != true)
                return;
            __state = new InfectionBefore(pawn, hediff);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterTick(bool __runOriginal, InfectionBefore? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(Unit))
            return;
        try
        {
            var pawn = __state.Pawn;
            var infectedDef = geneInfectedField?.GetValue(null) as HediffDef;
            if (infectedDef is null)
                return;

            var hediffSet = pawn.health?.hediffSet;
            bool liveAffected = !pawn.Dead && IsColonyAffected(pawn);
            bool removed = hediffSet is not null && !hediffSet.hediffs.Contains(__state.Hediff);
            bool hasInfected = hediffSet is not null && hediffSet.HasHediff(infectedDef);

            if (VreFungoidRules.ConversionCommitted(
                    severityReached: true, liveAffected, removed, hasInfected))
                OutcomeMetric.Record(Unit);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
