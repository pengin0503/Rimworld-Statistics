namespace RimworldStatistics.RimatomicsByproducts;

internal static class RimatomicsByproductRules
{
    internal static long Placed(int actualPlaced, bool playerOwned, bool processorMatch,
        bool normalUnload)
        => actualPlaced > 0 && playerOwned && processorMatch && normalUnload
            ? actualPlaced : 0;
}
