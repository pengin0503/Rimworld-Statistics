using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.SeventhWaveModOutcomes;

/// <summary>
/// Metrics for Prison Labor (avius.prisonlabor, 1.7/Assemblies/PrisonLabor.dll):
/// 1. Resocializations: observes ColumnWorker_Resocialization.ConvertPrisoner(Pawn)
///    when a colony prisoner with Need_Treatment.ResocializationReady == true transitions
///    into a free colony member via rehabilitation.
/// 2. Interrogation intel extracted: observes RitualOutcomeEffectWorker_Interrogation.Apply(...)
///    when a colony warden interrogates a colony prisoner and PrisonerComp.HasIntel
///    transitions from true to false upon generating an intel quest.
/// </summary>
internal static class PrisonLaborOutcomes
{
    private const string AssemblyName = "PrisonLabor";
    private const string ResocializationsUnit = "mod.avius.prisonlabor.resocializations";
    private const string InterrogationIntelUnit = "mod.avius.prisonlabor.interrogation_intel";

    private static Type needTreatmentType = null!;
    private static PropertyInfo resocializationReadyProperty = null!;
    private static Type prisonerCompType = null!;
    private static PropertyInfo hasIntelProperty = null!;

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            PrisonLaborRules.OwnerPackageId,
            "resocializations",
            "RWS_PrisonLaborResocializations",
            "RWS_PrisonLaborResocializationsDesc",
            MetricCategory.Population,
            InstallResocializations);

        yield return OutcomeMetric.Count(
            PrisonLaborRules.OwnerPackageId,
            "interrogation_intel",
            "RWS_PrisonLaborInterrogationIntel",
            "RWS_PrisonLaborInterrogationIntelDesc",
            MetricCategory.Ideology,
            InstallInterrogationIntel,
            dlc: AcquisitionUnits.Ideology);
    }

    private static void InstallResocializations(Harmony harmony)
    {
        var workerType = OutcomeMetric.Bind("PrisonLabor.Core.MainButton_Window.ColumnWorker_Resocialization", AssemblyName);
        needTreatmentType = OutcomeMetric.Bind("PrisonLabor.Core.Needs.Need_Treatment", AssemblyName);
        if (!typeof(Need).IsAssignableFrom(needTreatmentType))
            throw new InvalidOperationException("PrisonLabor Need_Treatment no longer derives from Need");

        resocializationReadyProperty = AccessTools.Property(needTreatmentType, "ResocializationReady") is { GetMethod: not null } prop &&
            prop.PropertyType == typeof(bool)
            ? prop
            : throw new InvalidOperationException("PrisonLabor Need_Treatment.ResocializationReady signature changed");

        var convert = OutcomeMetric.Method(workerType, "ConvertPrisoner", typeof(void), typeof(Pawn));
        OutcomeMetric.Patch(harmony, convert, typeof(PrisonLaborOutcomes), nameof(BeforeResocialization), nameof(AfterResocialization));
    }

    private static bool IsResocializationReady(Pawn pawn)
    {
        var needs = pawn.needs?.AllNeeds;
        if (needs is null)
            return false;
        for (var i = 0; i < needs.Count; i++)
        {
            var need = needs[i];
            if (need is not null && needTreatmentType.IsInstanceOfType(need))
                return resocializationReadyProperty.GetValue(need) is true;
        }
        return false;
    }

    private static void BeforeResocialization(Pawn? __0, out Pawn? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ResocializationsUnit) || __0 is null)
            return;

        try
        {
            if (PrisonLaborRules.IsEligibleResocializationBefore(
                    isPrisonerOfColony: GameFacts.IsPrisonerOfColonySafe(__0),
                    isDead: __0.Dead,
                    wasMember: GameFacts.IsMember(__0),
                    resocializationReady: IsResocializationReady(__0)))
            {
                __state = __0;
            }
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(ResocializationsUnit, error);
        }
    }

    private static void AfterResocialization(bool __runOriginal, Pawn? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(ResocializationsUnit))
            return;

        try
        {
            if (PrisonLaborRules.ShouldRecordResocialization(
                    wasEligibleBefore: true,
                    isDeadAfter: __state.Dead,
                    isPrisonerOfColonyAfter: GameFacts.IsPrisonerOfColonySafe(__state),
                    isMemberAfter: GameFacts.IsMember(__state)))
            {
                OutcomeMetric.Record(ResocializationsUnit);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(ResocializationsUnit, error);
        }
    }

    private static void InstallInterrogationIntel(Harmony harmony)
    {
        var ritualWorkerType = OutcomeMetric.Bind(
            "PrisonLabor.Core.Interrogation.Ritual.RitualOutcomeEffectWorker_Interrogation",
            AssemblyName);
        prisonerCompType = OutcomeMetric.Bind("PrisonLabor.Core.Components.PrisonerComp", AssemblyName);
        if (!typeof(ThingComp).IsAssignableFrom(prisonerCompType))
            throw new InvalidOperationException("PrisonLabor PrisonerComp no longer derives from ThingComp");

        hasIntelProperty = AccessTools.Property(prisonerCompType, "HasIntel") is { GetMethod: not null } prop &&
            prop.PropertyType == typeof(bool)
            ? prop
            : throw new InvalidOperationException("PrisonLabor PrisonerComp.HasIntel signature changed");

        var apply = OutcomeMetric.Method(
            ritualWorkerType,
            "Apply",
            typeof(void),
            typeof(float),
            typeof(Dictionary<Pawn, int>),
            typeof(LordJob_Ritual));
        OutcomeMetric.Patch(harmony, apply, typeof(PrisonLaborOutcomes), nameof(BeforeInterrogation), nameof(AfterInterrogation));
    }

    private static ThingComp? FindPrisonerComp(Pawn pawn)
    {
        var comps = pawn.AllComps;
        if (comps is null)
            return null;
        for (var i = 0; i < comps.Count; i++)
        {
            var comp = comps[i];
            if (comp is not null && prisonerCompType.IsInstanceOfType(comp))
                return comp;
        }
        return null;
    }

    private static void BeforeInterrogation(LordJob_Ritual? __2, out ThingComp? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(InterrogationIntelUnit) || __2 is null)
            return;

        try
        {
            var warden = __2.PawnWithRole("warden");
            var prisoner = __2.PawnWithRole("prisoner");
            if (warden is null || prisoner is null || FindPrisonerComp(prisoner) is not { } comp ||
                hasIntelProperty.GetValue(comp) is not bool hasIntelBefore)
                return;

            if (PrisonLaborRules.IsEligibleInterrogationBefore(
                    isWardenMember: GameFacts.IsMember(warden),
                    isPrisonerOfColony: GameFacts.IsPrisonerOfColonySafe(prisoner),
                    isPrisonerDead: prisoner.Dead,
                    hasIntelBefore: hasIntelBefore))
            {
                __state = comp;
            }
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(InterrogationIntelUnit, error);
        }
    }

    private static void AfterInterrogation(bool __runOriginal, ThingComp? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(InterrogationIntelUnit))
            return;

        try
        {
            if (hasIntelProperty.GetValue(__state) is bool hasIntelAfter &&
                PrisonLaborRules.ShouldRecordInterrogationIntel(wasEligibleBefore: true, hasIntelAfter: hasIntelAfter))
            {
                OutcomeMetric.Record(InterrogationIntelUnit);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(InterrogationIntelUnit, error);
        }
    }
}
