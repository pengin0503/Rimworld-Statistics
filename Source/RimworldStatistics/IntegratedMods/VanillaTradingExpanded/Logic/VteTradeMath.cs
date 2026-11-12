namespace RimworldStatistics.VanillaTradingExpandedAddon;

/// <summary>Game-independent value checks for VTE's completed operations, not UI intent.</summary>
internal static class VteTradeMath
{
    internal static int SettledAmount(int before, int after, bool credit)
        => (int)Math.Min(int.MaxValue, Math.Max(0L, credit ? (long)after - before : (long)before - after));

    internal static int FulfilledItems(int before, int after, int requested)
        => requested <= 0 ? 0 : (int)Math.Min(requested, Math.Max(0L, (long)before - after));

    internal static int ProportionalReward(int reward, int requested, int delivered)
    {
        if (reward <= 0 || requested <= 0 || delivered <= 0)
            return 0;
        return (int)Math.Clamp(reward * Math.Min(delivered, requested) / (float)requested, 0f, int.MaxValue);
    }

    /// <summary>Deterministic within a checkpoint branch. A different balance state is a new transaction.</summary>
    internal static string EventId(string bankOrContract, string action, int tick, int before, int after)
        => string.Concat("vte:", action, ":", bankOrContract, ":", tick, ":", before, ":", after);
}
