namespace RimworldStatistics.VqeDroneFactory;

internal static class VqeDroneRules
{
    internal static bool AssemblerSuccess(int counter, int target, bool newlySpawned,
        bool matchesKind, bool playerOwned)
        => counter > target && newlySpawned && matchesKind && playerOwned;

    internal static bool HatchSuccess(bool spawned, bool currentlyExists, bool playerOwned)
        => spawned && currentlyExists && playerOwned;
}
