namespace RimworldStatistics.PrisonExpandedParole;

/// <summary>Recognize actual prison outcomes, not hearing/exit orders or repeated ticks.</summary>
internal static class PrisonExpandedCompletionRules
{
    internal static bool Served(float sentenceDays, float beforeDays, float afterDays,
        bool paroleDueBefore, bool paroleDueAfter, bool livingPlayerPrisoner)
        => livingPlayerPrisoner && !paroleDueBefore && paroleDueAfter &&
           float.IsFinite(sentenceDays) && sentenceDays > 0f &&
           float.IsFinite(beforeDays) && beforeDays >= 0f && beforeDays < sentenceDays &&
           float.IsFinite(afterDays) && afterDays >= sentenceDays && afterDays > beforeDays;

    internal static bool Departed(bool playerPrisoner, bool tracked, bool home,
        bool spawnedBefore, bool spawnedAfter, bool stillHome, bool destroyed)
        => playerPrisoner && tracked && home && spawnedBefore &&
           !spawnedAfter && !stillHome && !destroyed;
}
