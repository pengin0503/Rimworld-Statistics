namespace RimworldStatistics.VreRecycling;

/// <summary>
/// Vanilla Recycling Expanded CompSuperSimpleProcessor output accounting:
/// handles all three branches of upstream TrySpawnAtCell without trusting its
/// buggy bool return (which falls through to false when overflowing an existing
/// stack via GenPlace.TryPlaceThing), and separates GenPlace placements from
/// direct existing-stack additions so outputs are never double-counted.
/// </summary>
internal static class VreRecyclingRules
{
    internal static long ConfirmedOutputCount(
        bool spawned,
        bool playerOwned,
        bool tryPlaceCalled,
        long placedActionCount,
        int existingBeforeCount,
        int existingAfterCount,
        bool existingDestroyed)
    {
        if (!spawned || !playerOwned)
            return 0;
        if (tryPlaceCalled)
            return placedActionCount > 0 ? placedActionCount : 0;
        if (existingDestroyed || existingAfterCount <= existingBeforeCount)
            return 0;
        return existingAfterCount - existingBeforeCount;
    }
}
