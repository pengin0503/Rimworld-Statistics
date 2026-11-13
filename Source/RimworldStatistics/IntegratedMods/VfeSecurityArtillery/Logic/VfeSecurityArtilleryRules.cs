namespace RimworldStatistics.VfeSecurityArtillery;

internal static class VfeSecurityArtilleryRules
{
    /// <summary>Arrival means a target-map projectile was actually launched, not a target order or queued travel.</summary>
    internal static bool LaunchedAtTarget(int before, int after, bool playerLauncher,
        bool offMap)
        => playerLauncher && offMap && after == before + 1;
}
