using RimworldStatistics.Core;

namespace RimworldStatistics.BiomesBees;

internal static class BiomesBeesRules
{
    internal const string OwnerPackageId = "BiomesTeam.BiomesBees";
    internal const string FrameworkPackageId = "BiomesTeam.CoreFramework";

    internal static string? ResolvePollinationBreakdown(
        bool isPlayerRelevant,
        float beforeGrowth,
        float afterGrowth,
        string? plantPackageId,
        string? plantDefName)
    {
        if (!isPlayerRelevant ||
            float.IsNaN(beforeGrowth) ||
            float.IsNaN(afterGrowth) ||
            afterGrowth <= beforeGrowth)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(plantPackageId, plantDefName);
    }
}
