namespace RimworldStatistics.SimpleWarrants;

internal static class SimpleWarrantsRules
{
    internal static long? SettledSilver(bool completed, bool removed, bool destroyed, long received)
        => completed && removed && destroyed ? Math.Max(0, received) : null;
}
