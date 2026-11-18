using RimworldStatistics.Core;

namespace RimworldStatistics.SeventhWaveModOutcomes;

internal static class HorrorsRules
{
    internal const string OwnerPackageId = "Mlie.Horrors";

    internal static string? ResolveDestroyedHiveKey(
        bool isKillFinalize,
        bool wasSpawnedBefore,
        bool isSpawnedAfter,
        bool isPlayerFactionHive,
        bool hasColonyPresenceOnMap,
        string? hivePackageId,
        string? hiveDefName) =>
        isKillFinalize &&
        wasSpawnedBefore &&
        !isSpawnedAfter &&
        !isPlayerFactionHive &&
        hasColonyPresenceOnMap
            ? OptionalTransitionRecording.QualifiedDefKey(hivePackageId, hiveDefName)
            : null;
}
