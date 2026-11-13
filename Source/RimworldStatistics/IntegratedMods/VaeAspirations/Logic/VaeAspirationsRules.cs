namespace RimworldStatistics.VaeAspirations;

/// <summary>Rejects attempts, debug completion and already-completed aspirations.</summary>
internal static class VaeAspirationsRules
{
    internal static bool CanComplete(bool assigned, bool completed, bool normalScope, bool debug)
        => assigned && !completed && normalScope && !debug;
    internal static bool Completed(bool before, bool after) => !before && after;
}
