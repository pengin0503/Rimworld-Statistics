using RimworldStatistics.Core;

namespace RimworldStatistics.SeventhWaveModOutcomes;

internal static class RimQuestRules
{
    internal const string OwnerPackageId = "Mlie.RimQuest";

    internal static bool IsEligiblePurchaseBefore(
        bool isPlayerInteractor,
        bool hasSelectedQuestDef,
        bool isQuestPawnTrackedBefore) =>
        isPlayerInteractor && hasSelectedQuestDef && isQuestPawnTrackedBefore;

    internal static bool ShouldRecordPurchase(
        bool wasEligibleBefore,
        bool isQuestPawnTrackedAfter) =>
        wasEligibleBefore && !isQuestPawnTrackedAfter;

    internal static string? ResolvePurchasedQuestKey(
        bool purchased,
        string? questPackageId,
        string? questDefName) =>
        purchased ? OptionalTransitionRecording.QualifiedDefKey(questPackageId, questDefName) : null;

    internal static long AcceptedSilverSpent(bool purchased, int actualSilverCost) =>
        purchased && actualSilverCost > 0 ? actualSilverCost : 0L;
}
