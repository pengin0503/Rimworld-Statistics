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
/// Metrics for MedPod (sumghai.Medpod, 1.7/Assemblies/MedPod.dll):
/// Observes Building_BedMedPod.DischargePatient(Pawn, bool) and records a completed
/// automated treatment cycle only when a player-owned MedPod on a player home map
/// was in MedPodStatus.HealingFinished before discharge and finishTreatmentNormally == true
/// (excluding power-loss aborts, manual interruptions, despawns, and empty diagnoses).
/// </summary>
internal static class MedPodOutcomes
{
    private const string AssemblyName = "MedPod";
    private const string TreatmentsCompletedUnit = "mod.sumghai.medpod.treatments_completed";

    private static FieldInfo statusField = null!;

    private sealed record DischargeSnapshot(Pawn Patient, ThingDef? PodDef);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            MedPodRules.OwnerPackageId,
            "treatments_completed",
            "RWS_MedPodTreatmentsCompleted",
            "RWS_MedPodTreatmentsCompletedDesc",
            MetricCategory.Medicine,
            InstallTreatments,
            breakdown: true);
    }

    private static void InstallTreatments(Harmony harmony)
    {
        var podType = OutcomeMetric.Bind("MedPod.Building_BedMedPod", AssemblyName);
        var statusType = OutcomeMetric.Bind("MedPod.Building_BedMedPod+MedPodStatus", AssemblyName);
        if (!typeof(Building_Bed).IsAssignableFrom(podType) || !statusType.IsEnum)
            throw new InvalidOperationException("MedPod Building_BedMedPod or MedPodStatus type hierarchy changed");

        if (!Enum.IsDefined(statusType, "HealingFinished") ||
            Convert.ToInt32(Enum.Parse(statusType, "HealingFinished")) != MedPodRules.HealingFinishedStatus)
            throw new InvalidOperationException("MedPod MedPodStatus.HealingFinished enum value changed");

        statusField = AccessTools.DeclaredField(podType, "status") is { } sf && sf.FieldType == statusType
            ? sf
            : throw new InvalidOperationException("MedPod Building_BedMedPod.status field signature changed");

        var discharge = OutcomeMetric.Method(podType, "DischargePatient", typeof(void), typeof(Pawn), typeof(bool));
        OutcomeMetric.Patch(harmony, discharge, typeof(MedPodOutcomes), nameof(BeforeDischarge), nameof(AfterDischarge));
    }

    private static void BeforeDischarge(Building_Bed __instance, Pawn? __0, bool __1, out DischargeSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(TreatmentsCompletedUnit) ||
            __instance is null ||
            __0 is null)
            return;

        try
        {
            if (statusField.GetValue(__instance) is not Enum statusEnum)
                return;

            var statusValue = Convert.ToInt32(statusEnum);
            var isSpawnedHomePod = __instance is { Spawned: true, Destroyed: false, Map.IsPlayerHome: true };
            if (MedPodRules.IsEligibleDischargeBefore(
                    isSpawnedPlayerHomePod: isSpawnedHomePod,
                    isPlayerPod: GameFacts.IsPlayer(__instance),
                    isPatientAlive: !__0.Dead,
                    finishTreatmentNormally: __1,
                    statusBefore: statusValue))
            {
                __state = new DischargeSnapshot(__0, __instance.def);
            }
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(TreatmentsCompletedUnit, error);
        }
    }

    private static void AfterDischarge(bool __runOriginal, DischargeSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(TreatmentsCompletedUnit))
            return;

        try
        {
            var key = MedPodRules.ResolveCompletedPodKey(
                wasEligibleBefore: true,
                isPatientAliveAfter: !__state.Patient.Dead,
                podPackageId: __state.PodDef?.modContentPack?.PackageId,
                podDefName: __state.PodDef?.defName);
            if (key is not null)
            {
                GameFacts.Record(TreatmentsCompletedUnit, state => OptionalTransitionRecording.RecordCount(
                    state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(TreatmentsCompletedUnit, error);
        }
    }
}
