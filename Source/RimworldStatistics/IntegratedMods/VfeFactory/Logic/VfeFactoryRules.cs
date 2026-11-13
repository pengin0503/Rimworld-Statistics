namespace RimworldStatistics.VfeFactory;

internal static class VfeFactoryRules
{
    internal static long BufferYield(long before, long after)
        => after > before ? after - before : 0L;

    internal static bool Sown(bool newPlant, bool sown, bool kindMatches, bool playerOwned)
        => newPlant && sown && kindMatches && playerOwned;
}
