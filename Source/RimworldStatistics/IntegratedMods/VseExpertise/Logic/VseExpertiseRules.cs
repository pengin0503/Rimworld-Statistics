namespace RimworldStatistics.VseExpertise;

internal static class VseExpertiseRules
{
    internal static bool NewSelection(int before, int after, bool normalPanel)
        => normalPanel && after == before + 1;
    internal static long EarnedLevels(int before, int after)
        => after > before ? (long)after - before : 0;
}
