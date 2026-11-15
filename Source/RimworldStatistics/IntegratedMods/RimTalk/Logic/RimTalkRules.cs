using RimworldStatistics.Core;

namespace RimworldStatistics.RimTalk;

internal static class RimTalkRules
{
    internal const string OwnerPackageId = "cj.rimtalk";

    /// <summary>
    /// Resolves the breakdown key from a committed RimTalk conversation's player participation
    /// and safe TalkType enum category. Dialogue text, prompts, and pawn identifiers are never
    /// inspected or stored.
    /// </summary>
    internal static string? ResolveConversationBreakdown(
        bool initiatorIsPlayer,
        bool recipientIsPlayer,
        string? talkTypeName)
    {
        if (!initiatorIsPlayer && !recipientIsPlayer)
            return null;

        var category = string.IsNullOrWhiteSpace(talkTypeName) ? "Other" : talkTypeName.Trim();
        return OptionalTransitionRecording.QualifiedDefKey(OwnerPackageId, category);
    }
}
