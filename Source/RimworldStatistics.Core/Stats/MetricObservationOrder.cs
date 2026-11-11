namespace RimworldStatistics.Core;

public static class MetricObservationOrder
{
    public static MetricObservation? Latest(IEnumerable<MetricObservation> observations)
        => observations.OrderByDescending(o => o.SaveTick).ThenByDescending(o => o.ObservedUtc).ThenBy(o => o.Key, StringComparer.Ordinal).FirstOrDefault();
}
