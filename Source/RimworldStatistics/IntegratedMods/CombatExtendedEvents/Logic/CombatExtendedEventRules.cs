namespace RimworldStatistics.CombatExtendedEvents;

internal static class CombatExtendedEventRules
{
    internal static long MagazineRoundsUsed(int before, int after, bool owned,
        bool ammoMode)
        => owned && ammoMode && before > after && after >= 0 ? (long)before - after : 0;

    internal static long DirectRoundUsed(bool beforeDestroyed, bool afterDestroyed,
        bool owned, bool ammoMode)
        => owned && ammoMode && !beforeDestroyed && afterDestroyed ? 1L : 0L;

    internal static bool Suppressed(bool before, bool after, bool playerOwned,
        bool debugScope)
        => !before && after && playerOwned && !debugScope;
}
