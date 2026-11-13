namespace RimworldStatistics.VfePirateHiring;

internal static class VfePirateHiringRules
{
    internal static bool ContractCreated(bool pirateFaction, bool changedPawnList,
        bool nowHasPawns) => pirateFaction && changedPawnList && nowHasPawns;

    internal static long SilverPaid(long before, long after, bool contractCreated)
        => contractCreated && before > after && after >= 0 ? before - after : 0;
}
