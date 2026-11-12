using System.Globalization;

namespace RimworldStatistics.HospitalityAddon;

internal static class HospitalityPurchaseMath
{
    internal static (long Units, long Silver) Completed(IReadOnlyDictionary<int, int> before,
        IReadOnlyDictionary<int, int> after, long beforeSilver, long afterSilver, bool eligible)
    {
        if (!eligible) return (0, 0);
        long units = 0;
        foreach (var (id, count) in after)
            units += Math.Max(0, count - before.GetValueOrDefault(id));
        return units > 0 ? (units, Math.Max(0, beforeSilver - afterSilver)) : (0, 0);
    }

    internal static string? EventId(int loadId, int startTick) =>
        loadId < 0 || startTick < 0 ? null : string.Create(CultureInfo.InvariantCulture, $"shopping:{loadId}:{startTick}");
}
