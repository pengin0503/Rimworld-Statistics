using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.SeventhWaveModOutcomes;

/// <summary>
/// Metrics for Alpha Implants (sarg.alphaimplants, 1.6/Assemblies/AlphaImplants.dll selected by v1.7):
/// 1. Implant abilities activated: observes non-Psycast RimWorld.Ability.Activate overloads
///    for AbilityDefs owned by sarg.alphaimplants when cast by player pawns or colony animals
///    and confirmed by a lastCastTick change + true return value.
/// 2. Permanent scars cured: observes AlphaImplants.Hediff_CureScars.PostMake() and counts
///    the exact decrease in permanent injuries on player pawns, colony animals, or colony prisoners.
/// </summary>
internal static class AlphaImplantsOutcomes
{
    private const string AssemblyName = "AlphaImplants";
    private const string ImplantAbilitiesUnit = "mod.sarg.alphaimplants.implant_abilities";
    private const string ScarsCuredUnit = "mod.sarg.alphaimplants.scars_cured";

    private static FieldInfo lastCastTickField = null!;

    private sealed record ScarCureSnapshot(Pawn Patient, int PermanentInjuriesBefore);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            AlphaImplantsRules.OwnerPackageId,
            "implant_abilities",
            "RWS_AlphaImplantsAbilitiesActivated",
            "RWS_AlphaImplantsAbilitiesActivatedDesc",
            MetricCategory.Combat,
            InstallImplantAbilities,
            breakdown: true,
            dependencies: AlphaImplantsRules.VfeCorePackageId);

        yield return OutcomeMetric.Count(
            AlphaImplantsRules.OwnerPackageId,
            "scars_cured",
            "RWS_AlphaImplantsScarsCured",
            "RWS_AlphaImplantsScarsCuredDesc",
            MetricCategory.Medicine,
            InstallScarsCured,
            dependencies: AlphaImplantsRules.VfeCorePackageId);
    }

    private static void InstallImplantAbilities(Harmony harmony)
    {
        _ = OutcomeMetric.Bind("AlphaImplants.Hediff_CureScars", AssemblyName);
        lastCastTickField = AccessTools.Field(typeof(Ability), "lastCastTick") is { } lf && lf.FieldType == typeof(int)
            ? lf
            : throw new InvalidOperationException("RimWorld.Ability.lastCastTick field signature changed");

        var localActivate = OutcomeMetric.Method(
            typeof(Ability),
            nameof(Ability.Activate),
            typeof(bool),
            typeof(LocalTargetInfo),
            typeof(LocalTargetInfo));
        var globalActivate = OutcomeMetric.Method(
            typeof(Ability),
            nameof(Ability.Activate),
            typeof(bool),
            typeof(GlobalTargetInfo));

        var patcher = new ScopedHarmonyPatcher(harmony);
        patcher.Patch(localActivate, typeof(AlphaImplantsOutcomes), nameof(BeforeAbilityActivate), nameof(AfterAbilityActivate));
        patcher.Patch(globalActivate, typeof(AlphaImplantsOutcomes), nameof(BeforeAbilityActivate), nameof(AfterAbilityActivate));
    }

    private static void BeforeAbilityActivate(Ability __instance, out int? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ImplantAbilitiesUnit) || __instance is null)
            return;

        try
        {
            if (!AlphaImplantsRules.IsEligibleImplantAbilityBefore(
                    isPsycast: __instance is Psycast,
                    isPlayerCaster: GameFacts.IsPlayer(__instance.pawn),
                    abilityPackageId: __instance.def?.modContentPack?.PackageId))
                return;

            if (lastCastTickField.GetValue(__instance) is int tickBefore)
                __state = tickBefore;
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(ImplantAbilitiesUnit, error);
        }
    }

    private static void AfterAbilityActivate(Ability __instance, bool __result, bool __runOriginal, int? __state)
    {
        if (!__runOriginal || __state is not { } tickBefore || !OutcomeMetric.Observing(ImplantAbilitiesUnit))
            return;

        try
        {
            if (lastCastTickField.GetValue(__instance) is not int tickAfter)
                return;

            var key = AlphaImplantsRules.ResolveActivatedAbilityKey(
                wasEligibleBefore: true,
                activationResult: __result,
                lastCastTickBefore: tickBefore,
                lastCastTickAfter: tickAfter,
                abilityPackageId: __instance.def?.modContentPack?.PackageId,
                abilityDefName: __instance.def?.defName);
            if (key is not null)
            {
                GameFacts.Record(ImplantAbilitiesUnit, state => OptionalTransitionRecording.RecordCount(
                    state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(ImplantAbilitiesUnit, error);
        }
    }

    private static void InstallScarsCured(Harmony harmony)
    {
        var cureType = OutcomeMetric.Bind("AlphaImplants.Hediff_CureScars", AssemblyName);
        if (!typeof(Hediff).IsAssignableFrom(cureType))
            throw new InvalidOperationException("AlphaImplants.Hediff_CureScars no longer derives from Hediff");

        var postMake = OutcomeMetric.Method(cureType, "PostMake", typeof(void));
        OutcomeMetric.Patch(harmony, postMake, typeof(AlphaImplantsOutcomes), nameof(BeforeCureScars), nameof(AfterCureScars));
    }

    private static int CountPermanentInjuries(Pawn pawn)
    {
        var hediffs = pawn.health?.hediffSet?.hediffs;
        if (hediffs is null)
            return 0;
        var count = 0;
        for (var i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is Hediff_Injury injury && injury.IsPermanent())
                count++;
        }
        return count;
    }

    private static void BeforeCureScars(Hediff __instance, out ScarCureSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ScarsCuredUnit) || __instance?.pawn is not { } pawn)
            return;

        try
        {
            if (!AlphaImplantsRules.IsEligibleScarCurePatient(
                    isAlive: !pawn.Dead,
                    isPlayerOwned: GameFacts.IsPlayer(pawn),
                    isPrisonerOfColony: GameFacts.IsPrisonerOfColonySafe(pawn)))
                return;

            var before = CountPermanentInjuries(pawn);
            if (before > 0)
                __state = new ScarCureSnapshot(pawn, before);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(ScarsCuredUnit, error);
        }
    }

    private static void AfterCureScars(bool __runOriginal, ScarCureSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(ScarsCuredUnit))
            return;

        try
        {
            if (__state.Patient.Dead)
                return;

            var after = CountPermanentInjuries(__state.Patient);
            var cured = AlphaImplantsRules.ComputeCuredScars(__state.PermanentInjuriesBefore, after);
            if (cured > 0)
                OutcomeMetric.Record(ScarsCuredUnit, amount: cured);
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(ScarsCuredUnit, error);
        }
    }
}
