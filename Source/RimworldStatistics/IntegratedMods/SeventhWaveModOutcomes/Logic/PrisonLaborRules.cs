using RimworldStatistics.Core;

namespace RimworldStatistics.SeventhWaveModOutcomes;

internal static class PrisonLaborRules
{
    internal const string OwnerPackageId = "avius.prisonlabor";

    internal static bool IsEligibleResocializationBefore(
        bool isPrisonerOfColony,
        bool isDead,
        bool wasMember,
        bool resocializationReady) =>
        isPrisonerOfColony && !isDead && !wasMember && resocializationReady;

    internal static bool ShouldRecordResocialization(
        bool wasEligibleBefore,
        bool isDeadAfter,
        bool isPrisonerOfColonyAfter,
        bool isMemberAfter) =>
        wasEligibleBefore && !isDeadAfter && !isPrisonerOfColonyAfter && isMemberAfter;

    internal static bool IsEligibleInterrogationBefore(
        bool isWardenMember,
        bool isPrisonerOfColony,
        bool isPrisonerDead,
        bool hasIntelBefore) =>
        isWardenMember && isPrisonerOfColony && !isPrisonerDead && hasIntelBefore;

    internal static bool ShouldRecordInterrogationIntel(
        bool wasEligibleBefore,
        bool hasIntelAfter) =>
        wasEligibleBefore && !hasIntelAfter;
}
