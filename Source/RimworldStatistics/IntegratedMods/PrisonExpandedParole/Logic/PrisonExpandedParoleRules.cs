namespace RimworldStatistics.PrisonExpandedParole;

/// <summary>Approval is a committed Release action, not a prisoner's departure.</summary>
internal static class PrisonExpandedParoleRules
{
    internal static bool Approved(bool paroleWasDue, bool livingPlayerPrisoner,
        bool alreadyReleaseMode, bool paroleNowCleared, double sentenceDays,
        bool releaseMode)
        => paroleWasDue && livingPlayerPrisoner && !alreadyReleaseMode &&
           paroleNowCleared && sentenceDays == 0d && releaseMode;
}
