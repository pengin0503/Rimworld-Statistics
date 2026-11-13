namespace RimworldStatistics.VgeHybridOutcomes;

internal static class VgeHybridOutcomeRules
{
    internal static bool HybridBorn(bool ready, bool successfulRecipe,
        bool playerOwned, bool actuallySpawned)
        => ready && successfulRecipe && playerOwned && actuallySpawned;

    internal static bool MechTransformed(bool completed, bool originalGone,
        bool newPawnPlayer, bool newPawnSpawned)
        => completed && originalGone && newPawnPlayer && newPawnSpawned;
}
