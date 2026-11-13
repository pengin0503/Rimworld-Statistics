namespace RimworldStatistics.VceDeepchem;

/// <summary>Use actual accepted pipe-storage units, never requested production or capacity.</summary>
internal static class VceDeepchemRules
{
    internal static double AcceptedUnits(float stored, bool matchingPipe, bool playerOwned)
        => matchingPipe && playerOwned && float.IsFinite(stored) && stored > 0f
            ? stored : 0d;
}
