using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RimworldStatistics.VanillaTradingExpandedAddon;

/// <summary>Game-independent value checks for VTE's completed operations, not UI intent.</summary>
internal static class VteTradeMath
{
    internal static int SettledAmount(int before, int after, bool credit)
        => (int)Math.Min(int.MaxValue, Math.Max(0L, credit ? (long)after - before : (long)before - after));

    internal static int FulfilledItems(int before, int after, int requested)
        => requested <= 0 ? 0 : (int)Math.Min(requested, Math.Max(0L, (long)before - after));

    /// <summary>
    /// Compare only the original candidate stacks. Re-querying a limited result
    /// after consumption could pull in untouched reserve stacks and hide the sale.
    /// </summary>
    internal static int ConsumedSelected(IEnumerable<(int Before, int After)> stacks, int requested)
    {
        if (requested <= 0) return 0;
        long consumed = 0;
        foreach (var (before, after) in stacks)
            consumed += Math.Max(0L, (long)before - after);
        return (int)Math.Min(requested, Math.Min(consumed, int.MaxValue));
    }

    /// <summary>Player-posted contracts settle only after full bank payment and item generation.</summary>
    internal static bool PaidPurchaseCompleted(int cost, long plannedSpend, int requested, int remaining)
        => cost > 0 && plannedSpend == cost && requested > 0 && remaining == 0;

    internal static int ProportionalReward(int reward, int requested, int delivered)
    {
        if (reward <= 0 || requested <= 0 || delivered <= 0)
            return 0;
        return (int)Math.Clamp(reward * Math.Min(delivered, requested) / (float)requested, 0f, int.MaxValue);
    }

    /// <summary>
    /// Deterministic and bounded for SDK's 128-char limit. The SHA-256 digest
    /// prevents long DefNames and faction IDs from invalidating a confirmed event.
    /// </summary>
    internal static string EventId(string bankOrContract, string action, int tick, int before, int after)
    {
        var raw = string.Concat(bankOrContract.Length.ToString(CultureInfo.InvariantCulture), ":", bankOrContract,
            action.Length.ToString(CultureInfo.InvariantCulture), ":", action, ":",
            tick.ToString(CultureInfo.InvariantCulture), ":",
            before.ToString(CultureInfo.InvariantCulture), ":",
            after.ToString(CultureInfo.InvariantCulture));
        return "vte:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
