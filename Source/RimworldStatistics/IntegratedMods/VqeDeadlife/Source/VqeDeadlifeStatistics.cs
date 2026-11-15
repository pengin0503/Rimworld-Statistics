using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VqeDeadlife;

/// <summary>
/// Vanilla Quests Expanded - Deadlife:
/// 1) Counts ancient silo planning desk decryptions completed via StudiableBuilding_NewSilo.Study.
/// 2) Counts burnout generator kickstarts completed via CompInteractablePowerPlant.OnInteracted.
/// </summary>
internal static class VqeDeadlifeStatistics
{
    private const string AssemblyName = "VanillaQuestsExpandedDeadlife";
    private const string AnomalyPackageId = "ludeon.rimworld.anomaly";
    private const string SiloUnit = "mod.vanillaquestsexpanded.deadlife.silo_decryptions";
    private const string PowerUnit = "mod.vanillaquestsexpanded.deadlife.power_kickstarts";

    private static MethodInfo? kickstartCompGetter;
    private static FieldInfo? kickstartActiveField;

    private sealed record SiloAttempt(ThingDef Def);
    private sealed record PowerAttempt(ThingDef Def, object KickstartComp, bool WasActive);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: VqeDeadlifeRules.OwnerPackageId,
            family: "silo_decryptions",
            label: "RWS_VqeDeadlife_SiloDecryptions",
            description: "RWS_VqeDeadlife_SiloDecryptionsDesc",
            category: MetricCategory.Exploration,
            install: InstallSilo,
            breakdown: true,
            dlc: AnomalyPackageId,
            dependencies: VqeDeadlifeRules.FrameworkPackageId),
        OutcomeMetric.Count(
            owner: VqeDeadlifeRules.OwnerPackageId,
            family: "power_kickstarts",
            label: "RWS_VqeDeadlife_PowerKickstarts",
            description: "RWS_VqeDeadlife_PowerKickstartsDesc",
            category: MetricCategory.Colony,
            install: InstallPower,
            breakdown: true,
            dlc: AnomalyPackageId,
            dependencies: VqeDeadlifeRules.FrameworkPackageId),
    ];

    private static void InstallSilo(Harmony harmony)
    {
        var siloType = OutcomeMetric.Bind("VanillaQuestsExpandedDeadlife.StudiableBuilding_NewSilo", AssemblyName);
        if (!typeof(Building).IsAssignableFrom(siloType))
            throw new InvalidOperationException("VanillaQuestsExpandedDeadlife.StudiableBuilding_NewSilo type changed");

        var study = OutcomeMetric.Method(siloType, "Study", typeof(void), typeof(Pawn));
        OutcomeMetric.Patch(harmony, study, typeof(VqeDeadlifeStatistics), nameof(BeforeSiloStudy), nameof(AfterSiloStudy));
    }

    private static void InstallPower(Harmony harmony)
    {
        var interactableType = OutcomeMetric.Bind("VanillaQuestsExpandedDeadlife.CompInteractablePowerPlant", AssemblyName);
        var kickstartType = OutcomeMetric.Bind("VanillaQuestsExpandedDeadlife.CompKickstartablePowerPlant", AssemblyName);
        if (!typeof(ThingComp).IsAssignableFrom(interactableType) ||
            !typeof(ThingComp).IsAssignableFrom(kickstartType))
            throw new InvalidOperationException("VanillaQuestsExpandedDeadlife power comp type contract changed");

        var getter = AccessTools.PropertyGetter(interactableType, "compKickstartablePowerPlant");
        var activeField = AccessTools.DeclaredField(kickstartType, "active");
        if (getter is null || getter.ReturnType != kickstartType ||
            activeField is null || activeField.FieldType != typeof(bool))
            throw new InvalidOperationException("VanillaQuestsExpandedDeadlife power comp field contract changed");

        var onInteracted = OutcomeMetric.Method(interactableType, "OnInteracted", typeof(void), typeof(Pawn));

        kickstartCompGetter = getter;
        kickstartActiveField = activeField;
        try
        {
            OutcomeMetric.Patch(harmony, onInteracted, typeof(VqeDeadlifeStatistics), nameof(BeforePowerKickstart), nameof(AfterPowerKickstart));
        }
        catch
        {
            kickstartCompGetter = null;
            kickstartActiveField = null;
            throw;
        }
    }

    private static void BeforeSiloStudy(Building? __instance, Pawn? pawn, out SiloAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(SiloUnit) || __instance is not { Spawned: true } || __instance.def is null || pawn is null)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null || pawn.Faction != player)
                return;

            __state = new SiloAttempt(__instance.def);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(SiloUnit, e);
        }
    }

    private static void AfterSiloStudy(bool __runOriginal, SiloAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(SiloUnit))
            return;

        try
        {
            var key = VqeDeadlifeRules.ResolveSiloStudyBreakdown(
                actorIsPlayer: true,
                buildingValid: true,
                __state.Def.modContentPack?.PackageId,
                __state.Def.defName);

            if (key is not null)
            {
                GameFacts.Record(SiloUnit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(SiloUnit, e);
        }
    }

    private static void BeforePowerKickstart(ThingComp? __instance, Pawn? caster, out PowerAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(PowerUnit) ||
            __instance?.parent is not { Spawned: true } parent ||
            parent.def is null ||
            caster is null ||
            kickstartCompGetter is null ||
            kickstartActiveField is null)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null || caster.Faction != player)
                return;

            var kickstartComp = kickstartCompGetter.Invoke(__instance, null);
            if (kickstartComp is null)
                return;

            var wasActive = Convert.ToBoolean(kickstartActiveField.GetValue(kickstartComp));
            if (wasActive)
                return;

            __state = new PowerAttempt(parent.def, kickstartComp, false);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(PowerUnit, e);
        }
    }

    private static void AfterPowerKickstart(bool __runOriginal, PowerAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(PowerUnit) || kickstartActiveField is null)
            return;

        try
        {
            var isActiveAfter = Convert.ToBoolean(kickstartActiveField.GetValue(__state.KickstartComp));
            var key = VqeDeadlifeRules.ResolvePowerKickstartBreakdown(
                actorIsPlayer: true,
                buildingValid: true,
                __state.WasActive,
                isActiveAfter,
                __state.Def.modContentPack?.PackageId,
                __state.Def.defName);

            if (key is not null)
            {
                GameFacts.Record(PowerUnit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(PowerUnit, e);
        }
    }
}
