using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metrics for Altered Carbon 2: ReSleeved (hlx.UltratechAlteredCarbon, 1.6/Assemblies/AlteredCarbon.dll):
/// 1. Sleeve vat growth completions: Building_SleeveGestator.FinishGrowth transitioning
///    incubatorState from Growing to Inactive with a live sleeve in a player home gestator
///    (distinct from Biotech Building_GrowthVat and from surgical Resleeving).
/// 2. Needlecast mind transfers: Hediff_RemoteStack.Needlecast(INeedlecastable) transitioning
///    Needlecasted from false to true onto a live player-controlled target sleeve.
/// </summary>
internal static class AlteredCarbonStage2Outcomes
{
    private const string AssemblyName = "AlteredCarbon";
    private const string SleeveGrowthUnit = "mod.hlx.ultratechalteredcarbon.sleeve_growth";
    private const string NeedlecastUnit = "mod.hlx.ultratechalteredcarbon.needlecasts";

    private static FieldInfo incubatorStateField = null!;
    private static PropertyInfo innerPawnProperty = null!;
    private static object growingStateValue = null!;
    private static object inactiveStateValue = null!;
    private static PropertyInfo needlecastedProperty = null!;

    private sealed record GrowthAttempt(Building Gestator);
    private sealed record NeedlecastAttempt(Hediff RemoteStack, Pawn Target);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            AlteredCarbonStage2Rules.OwnerPackageId,
            "sleeve_growth",
            "RWS_AlteredCarbonSleeveGrowth",
            "RWS_AlteredCarbonSleeveGrowthDesc",
            MetricCategory.Biotech,
            InstallSleeveGrowth,
            dlc: AlteredCarbonStage2Rules.BiotechPackageId,
            dependencies: [AlteredCarbonStage2Rules.FrameworkPackageId]);

        yield return OutcomeMetric.Count(
            AlteredCarbonStage2Rules.OwnerPackageId,
            "needlecasts",
            "RWS_AlteredCarbonNeedlecasts",
            "RWS_AlteredCarbonNeedlecastsDesc",
            MetricCategory.Biotech,
            InstallNeedlecasts,
            dlc: AlteredCarbonStage2Rules.BiotechPackageId,
            dependencies: [AlteredCarbonStage2Rules.FrameworkPackageId]);
    }

    private static void InstallSleeveGrowth(Harmony harmony)
    {
        var incubatorType = OutcomeMetric.Bind("AlteredCarbon.Building_Incubator", AssemblyName);
        var gestatorType = OutcomeMetric.Bind("AlteredCarbon.Building_SleeveGestator", AssemblyName);
        var stateType = OutcomeMetric.Bind("AlteredCarbon.IncubatorState", AssemblyName);

        if (!typeof(Building).IsAssignableFrom(incubatorType) || !incubatorType.IsAssignableFrom(gestatorType) || !stateType.IsEnum)
            throw new InvalidOperationException("AlteredCarbon sleeve gestator hierarchy changed");

        incubatorStateField = AccessTools.DeclaredField(incubatorType, "incubatorState") is { } sf && sf.FieldType == stateType
            ? sf
            : throw new InvalidOperationException("AlteredCarbon Building_Incubator.incubatorState signature changed");

        innerPawnProperty = AccessTools.DeclaredProperty(gestatorType, "InnerPawn") is { } ip && ip.PropertyType == typeof(Pawn)
            ? ip
            : throw new InvalidOperationException("AlteredCarbon Building_SleeveGestator.InnerPawn signature changed");

        growingStateValue = Enum.Parse(stateType, "Growing");
        inactiveStateValue = Enum.Parse(stateType, "Inactive");

        var finishGrowth = OutcomeMetric.Method(gestatorType, "FinishGrowth", typeof(void));
        OutcomeMetric.Patch(harmony, finishGrowth, typeof(AlteredCarbonStage2Outcomes), nameof(BeforeFinishGrowth), nameof(AfterFinishGrowth));
    }

    private static void BeforeFinishGrowth(Building __instance, out GrowthAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(SleeveGrowthUnit) ||
            __instance is not { Spawned: true, Map.IsPlayerHome: true } ||
            !GameFacts.IsPlayer(__instance))
            return;

        try
        {
            if (Equals(incubatorStateField.GetValue(__instance), growingStateValue))
                __state = new GrowthAttempt(__instance);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(SleeveGrowthUnit, error);
        }
    }

    private static void AfterFinishGrowth(bool __runOriginal, GrowthAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(SleeveGrowthUnit))
            return;

        try
        {
            var isInactive = Equals(incubatorStateField.GetValue(__state.Gestator), inactiveStateValue);
            var innerPawn = innerPawnProperty.GetValue(__state.Gestator) as Pawn;
            if (AlteredCarbonStage2Rules.ShouldRecordSleeveGrowth(
                    playerHomeVat: true,
                    wasGrowingStateBefore: true,
                    isInactiveStateAfter: isInactive,
                    innerSleeveAliveAfter: innerPawn is { Dead: false }))
            {
                OutcomeMetric.Record(SleeveGrowthUnit);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(SleeveGrowthUnit, error);
        }
    }

    private static void InstallNeedlecasts(Harmony harmony)
    {
        var remoteType = OutcomeMetric.Bind("AlteredCarbon.Hediff_RemoteStack", AssemblyName);
        var needlecastableType = OutcomeMetric.Bind("AlteredCarbon.INeedlecastable", AssemblyName);
        if (!typeof(Hediff).IsAssignableFrom(remoteType))
            throw new InvalidOperationException("AlteredCarbon Hediff_RemoteStack hierarchy changed");

        needlecastedProperty = AccessTools.DeclaredProperty(remoteType, "Needlecasted") is { } np && np.PropertyType == typeof(bool)
            ? np
            : throw new InvalidOperationException("AlteredCarbon Hediff_RemoteStack.Needlecasted signature changed");

        var needlecast = OutcomeMetric.Method(remoteType, "Needlecast", typeof(void), needlecastableType);
        OutcomeMetric.Patch(harmony, needlecast, typeof(AlteredCarbonStage2Outcomes), nameof(BeforeNeedlecast), nameof(AfterNeedlecast));
    }

    private static void BeforeNeedlecast(Hediff __instance, object? __0, out NeedlecastAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(NeedlecastUnit) || __0 is null || __instance?.pawn is not { Dead: false } target)
            return;

        try
        {
            if (needlecastedProperty.GetValue(__instance) is false)
                __state = new NeedlecastAttempt(__instance, target);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(NeedlecastUnit, error);
        }
    }

    private static void AfterNeedlecast(bool __runOriginal, NeedlecastAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(NeedlecastUnit))
            return;

        try
        {
            if (needlecastedProperty.GetValue(__state.RemoteStack) is bool needlecastedAfter &&
                AlteredCarbonStage2Rules.ShouldRecordNeedlecast(
                    wasNeedlecastedBefore: false,
                    targetAliveBefore: true,
                    isNeedlecastedAfter: needlecastedAfter,
                    targetAliveAfter: !__state.Target.Dead,
                    targetIsPlayerAfter: GameFacts.IsPlayer(__state.Target)))
            {
                OutcomeMetric.Record(NeedlecastUnit);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(NeedlecastUnit, error);
        }
    }
}
