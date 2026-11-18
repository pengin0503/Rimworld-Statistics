using RimworldStatistics.Core;

namespace RimworldStatistics.SeventhWaveModOutcomes;

internal static class MedPodRules
{
    internal const string OwnerPackageId = "sumghai.Medpod";
    internal const int HealingFinishedStatus = 4;

    internal static bool IsEligibleDischargeBefore(
        bool isSpawnedPlayerHomePod,
        bool isPlayerPod,
        bool isPatientAlive,
        bool finishTreatmentNormally,
        int statusBefore) =>
        isSpawnedPlayerHomePod &&
        isPlayerPod &&
        isPatientAlive &&
        finishTreatmentNormally &&
        statusBefore == HealingFinishedStatus;

    internal static string? ResolveCompletedPodKey(
        bool wasEligibleBefore,
        bool isPatientAliveAfter,
        string? podPackageId,
        string? podDefName) =>
        wasEligibleBefore && isPatientAliveAfter
            ? OptionalTransitionRecording.QualifiedDefKey(podPackageId, podDefName)
            : null;
}
