namespace RimworldStatistics.Core;

/// <summary>
/// Conservative 1.7 historical salvage for Anomaly codex discoveries and monolith progression
/// from <c>&lt;entityCodex&gt;&lt;discoveredEntities&gt;</c> and <c>GameComponent_Anomaly</c>.
/// When Anomaly is inactive or playstyles omit the monolith without positive progression,
/// no zero is fabricated.
/// </summary>
internal static class HistoricalAnomalyStateReader
{
    public const int MaxDiscoveredEntities = 4096;

    public static Dictionary<string, MetricEntry>? Build(
        IReadOnlyCollection<string>? discoveredEntities,
        bool entityCodexPresent,
        bool entityCodexMalformed,
        int anomalyComponentCount,
        bool anomalyComponentMalformed,
        int? highestLevelReached,
        string? monolithRef,
        IReadOnlyList<string>? activeDlcIds)
    {
        if (activeDlcIds?.Contains(AcquisitionUnits.Anomaly, StringComparer.OrdinalIgnoreCase) != true)
            return null;

        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);

        if (entityCodexPresent && !entityCodexMalformed
            && discoveredEntities is { Count: > 0 and <= MaxDiscoveredEntities })
        {
            var key = AnomalyStateSnapshot.DiscoveredEntities;
            result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
            {
                Coverage = MetricCoverage.Partial,
                IntegerValue = discoveredEntities.Count,
            });
        }

        if (anomalyComponentCount == 1 && !anomalyComponentMalformed)
        {
            var hasMonolith = !string.IsNullOrWhiteSpace(monolithRef) && monolithRef != "null";
            int? level = highestLevelReached switch
            {
                > 0 => highestLevelReached.Value,
                0 when hasMonolith => 0,
                null when hasMonolith => 0,
                _ => null,
            };
            if (level is { } confirmedLevel)
            {
                var key = AnomalyStateSnapshot.MonolithHighestLevel;
                result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
                {
                    Coverage = MetricCoverage.Partial,
                    IntegerValue = confirmedLevel,
                });
            }
        }

        return result.Count > 0 ? result : null;
    }
}
