namespace RimworldStatistics.VfeTribals;

/// <summary>Decisions independent of the optional Tribal game assembly.</summary>
internal static class VfeTribalsRules
{
    internal static bool EraAdvanced(int? oldLevel, int newLevel, int requestedLevel)
        => oldLevel is { } previous && newLevel > previous && newLevel == requestedLevel;

    internal static bool NewCornerstone(bool before, bool after) => !before && after;
}
