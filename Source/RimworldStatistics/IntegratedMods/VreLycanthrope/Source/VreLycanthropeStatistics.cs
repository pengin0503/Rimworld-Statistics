using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VreLycanthrope;

/// <summary>
/// Vanilla Races Expanded - Lycanthrope: counts completed morph transitions
/// (both manual ability activations and all automatic morph-condition triggers
/// converge on CompAbilityEffect_Morph.Apply) and committed germline reimplants.
/// </summary>
internal static class VreLycanthropeStatistics
{
    private const string Owner = "vanillaracesexpanded.lycanthrope";
    private const string AssemblyName = "VanillaRacesExpanded-Lycanthrope";
    private const string MorphUnit = "mod.vanillaracesexpanded.lycanthrope.morphs";
    private const string ReimplantUnit = "mod.vanillaracesexpanded.lycanthrope.germline_reimplants";

    private static FieldInfo? morphedField;
    private static FieldInfo? morphXenotypeField;
    private static FieldInfo? originalXenotypeField;
    private static FieldInfo? endogerminationComaField;

    private sealed record MorphBefore(Pawn Pawn, bool WasMorphed, XenotypeDef? BeforeXenotype);

    private sealed record ReimplantBefore(
        XenotypeDef? Xenotype,
        string? XenotypeName,
        HashSet<GeneDef> Endogenes,
        HashSet<GeneDef> Xenogenes,
        bool HadComa);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(Owner, "morphs",
            "RWS_VreLycanthrope_Morphs", "RWS_VreLycanthrope_MorphsDesc",
            MetricCategory.Biotech, InstallMorph, breakdown: true, dlc: AcquisitionUnits.Biotech),
        OutcomeMetric.Count(Owner, "germline_reimplants",
            "RWS_VreLycanthrope_GermlineReimplants", "RWS_VreLycanthrope_GermlineReimplantsDesc",
            MetricCategory.Biotech, InstallReimplant, breakdown: false, dlc: AcquisitionUnits.Biotech),
    ];

    private static void InstallMorph(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("VanillaRacesExpandedLycanthrope.CompAbilityEffect_Morph", AssemblyName);
        if (!typeof(CompAbilityEffect).IsAssignableFrom(type))
            throw new InvalidOperationException("Lycanthrope CompAbilityEffect_Morph base type changed");

        var morphed = AccessTools.DeclaredField(type, "morphed");
        var morphXenotype = AccessTools.DeclaredField(type, "xenotype");
        var originalXenotype = AccessTools.DeclaredField(type, "xenotypeOriginal");
        if (morphed?.FieldType != typeof(bool) ||
            morphXenotype?.FieldType != typeof(XenotypeDef) ||
            originalXenotype?.FieldType != typeof(XenotypeDef))
            throw new InvalidOperationException("Lycanthrope morph state fields changed");

        var apply = OutcomeMetric.Method(type, "Apply", typeof(void),
            typeof(LocalTargetInfo), typeof(LocalTargetInfo));

        morphedField = morphed;
        morphXenotypeField = morphXenotype;
        originalXenotypeField = originalXenotype;
        try
        {
            OutcomeMetric.Patch(harmony, apply, typeof(VreLycanthropeStatistics),
                nameof(BeforeMorph), nameof(AfterMorph));
        }
        catch
        {
            morphedField = null;
            morphXenotypeField = null;
            originalXenotypeField = null;
            throw;
        }
    }

    private static void InstallReimplant(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("VanillaRacesExpandedLycanthrope.CompAbilityEffect_ReimplantGermline",
            AssemblyName);
        if (!typeof(CompAbilityEffect).IsAssignableFrom(type))
            throw new InvalidOperationException("Lycanthrope CompAbilityEffect_ReimplantGermline base type changed");

        var defOf = OutcomeMetric.Bind("VanillaRacesExpandedLycanthrope.InternalDefOf", AssemblyName);
        var coma = AccessTools.DeclaredField(defOf, "VRE_EndogerminationComa");
        if (coma is null || !coma.IsStatic || coma.FieldType != typeof(HediffDef))
            throw new InvalidOperationException("Lycanthrope InternalDefOf.VRE_EndogerminationComa changed");

        var reimplant = OutcomeMetric.Method(type, "ReimplantGermline", typeof(void),
            typeof(Pawn), typeof(Pawn));

        endogerminationComaField = coma;
        try
        {
            OutcomeMetric.Patch(harmony, reimplant, typeof(VreLycanthropeStatistics),
                nameof(BeforeReimplant), nameof(AfterReimplant));
        }
        catch
        {
            endogerminationComaField = null;
            throw;
        }
    }

    private static void BeforeMorph(object __instance, out MorphBefore? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(MorphUnit))
            return;
        try
        {
            var pawn = (__instance as CompAbilityEffect)?.parent?.pawn;
            if (pawn is not { Dead: false, genes: not null } || !GameFacts.IsPlayer(pawn) ||
                morphedField?.GetValue(__instance) is not bool wasMorphed)
                return;
            __state = new MorphBefore(pawn, wasMorphed, pawn.genes.Xenotype);
        }
        catch (Exception e) { LiveMetrics.Fail(MorphUnit, e); }
    }

    private static void AfterMorph(object __instance, bool __runOriginal, MorphBefore? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(MorphUnit))
            return;
        try
        {
            var pawn = __state.Pawn;
            if (pawn.Dead || !GameFacts.IsPlayer(pawn) || pawn.genes is not { } genes ||
                morphedField?.GetValue(__instance) is not bool nowMorphed)
                return;

            var expected = (XenotypeDef?)(nowMorphed
                ? morphXenotypeField?.GetValue(__instance)
                : originalXenotypeField?.GetValue(__instance));
            var after = genes.Xenotype;
            bool expectedMatched = expected is not null && ReferenceEquals(after, expected);
            bool changed = !ReferenceEquals(__state.BeforeXenotype, after);

            if (!VreLycanthropeRules.MorphCommitted(__state.WasMorphed, nowMorphed,
                    expectedMatched, changed, livePlayerPawn: true))
                return;

            var key = VreLycanthropeRules.MorphBreakdownKey(nowMorphed,
                after!.modContentPack?.PackageId, after.defName, genes.xenotypeName);
            GameFacts.Record(MorphUnit, state => OptionalTransitionRecording.RecordCount(
                state, "completed", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(MorphUnit, e); }
    }

    private static void BeforeReimplant(Pawn? __0, Pawn? __1, out ReimplantBefore? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ReimplantUnit))
            return;
        try
        {
            if (__0 is not { Dead: false, genes: not null } caster ||
                __1 is not { Dead: false, genes: { } genes } recipient ||
                (!GameFacts.IsPlayer(caster) && !GameFacts.IsPlayer(recipient)))
                return;

            var comaDef = endogerminationComaField?.GetValue(null) as HediffDef;
            bool hadComa = comaDef is not null && recipient.health?.hediffSet?.HasHediff(comaDef) == true;
            __state = new ReimplantBefore(
                genes.Xenotype,
                genes.xenotypeName,
                genes.Endogenes.Select(gene => gene.def).ToHashSet(),
                genes.Xenogenes.Select(gene => gene.def).ToHashSet(),
                hadComa);
        }
        catch (Exception e) { LiveMetrics.Fail(ReimplantUnit, e); }
    }

    private static void AfterReimplant(Pawn? __0, Pawn? __1, bool __runOriginal, ReimplantBefore? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(ReimplantUnit))
            return;
        try
        {
            if (__0 is not { } caster || __1 is not { Dead: false, genes: { } actual } recipient)
                return;
            bool playerInvolved = GameFacts.IsPlayer(caster) || GameFacts.IsPlayer(recipient);
            if (!playerInvolved)
                return;

            var comaDef = endogerminationComaField?.GetValue(null) as HediffDef;
            bool comaAdded = comaDef is not null && !__state.HadComa &&
                recipient.health?.hediffSet?.HasHediff(comaDef) == true;
            bool genesChanged = __state.Xenotype != actual.Xenotype ||
                !string.Equals(__state.XenotypeName, actual.xenotypeName, StringComparison.Ordinal) ||
                !__state.Endogenes.SetEquals(actual.Endogenes.Select(gene => gene.def)) ||
                !__state.Xenogenes.SetEquals(actual.Xenogenes.Select(gene => gene.def));

            if (VreLycanthropeRules.ReimplantCommitted(
                    liveRecipient: true, playerInvolved: true, genesOrComaApplied: comaAdded || genesChanged))
                OutcomeMetric.Record(ReimplantUnit);
        }
        catch (Exception e) { LiveMetrics.Fail(ReimplantUnit, e); }
    }
}
