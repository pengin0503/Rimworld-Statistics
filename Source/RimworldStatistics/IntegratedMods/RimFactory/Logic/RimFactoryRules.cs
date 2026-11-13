namespace RimworldStatistics.RimFactory;

internal static class RimFactoryRules
{
    internal static long PlacedQuantity(int startingStack, long confirmed)
        => Math.Max(0, Math.Min(startingStack, confirmed));
}
