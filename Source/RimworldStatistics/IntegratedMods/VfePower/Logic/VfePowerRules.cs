namespace RimworldStatistics.VfePower;

/// <summary>
/// Vanilla Furniture Expanded - Power: a nuclear reactor meltdown is counted once
/// when signalMeltdown transitions from false to true on a player-owned spawned
/// building and its breakdownable component is broken down. Reloading an already
/// melted-down reactor or breaking down due to a solar flare contributes zero.
/// </summary>
internal static class VfePowerRules
{
    internal static bool MeltdownCommitted(
        bool wasSignaled,
        bool isSignaled,
        bool isBrokenDown,
        bool playerOwned,
        bool spawned)
        => !wasSignaled && isSignaled && isBrokenDown && playerOwned && spawned;
}
