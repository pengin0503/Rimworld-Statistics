namespace RimworldStatistics.VaeAchievements;

/// <summary>Success rules isolated from optional game types for unit tests.</summary>
internal static class VaeAchievementRules
{
    internal static bool NewUnlock(bool before, bool after, bool debug, bool initialization)
        => !debug && !initialization && !before && after;

    /// <summary>Null means credit could not be measured; it is not a known zero.</summary>
    internal static int? CreditedPoints(int? before, int? after)
        => before is { } a && after is { } b && b >= a ? b - a : null;
}
