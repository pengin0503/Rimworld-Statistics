namespace RimworldStatistics.VnpeGrinder;

internal static class VnpeGrinderRules
{
    internal static double AcceptedPaste(float stored, bool successful,
        bool matchingNet, bool playerOwned)
        => successful && matchingNet && playerOwned &&
           float.IsFinite(stored) && stored > 0f ? stored : 0d;
}
