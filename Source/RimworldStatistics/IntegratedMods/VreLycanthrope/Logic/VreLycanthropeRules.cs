using RimworldStatistics.Core;

namespace RimworldStatistics.VreLycanthrope;

/// <summary>
/// Confirms actual completed morph transitions and germline reimplants.
/// Custom original xenotypes keep a non-empty xenotypeName on reversion and
/// must fall back to null so Breakdown reports Partial instead of Baseliner.
/// </summary>
internal static class VreLycanthropeRules
{
    internal static bool MorphCommitted(bool wasMorphed, bool nowMorphed,
        bool expectedTargetMatched, bool xenotypeChanged, bool livePlayerPawn)
        => livePlayerPawn && wasMorphed != nowMorphed && expectedTargetMatched && xenotypeChanged;

    internal static string? MorphBreakdownKey(bool nowMorphed, string? packageId,
        string? defName, string? xenotypeName)
    {
        if (!nowMorphed && !string.IsNullOrEmpty(xenotypeName))
            return null;
        return OptionalTransitionRecording.QualifiedDefKey(packageId, defName);
    }

    internal static bool ReimplantCommitted(bool liveRecipient, bool playerInvolved,
        bool genesOrComaApplied)
        => liveRecipient && playerInvolved && genesOrComaApplied;
}
