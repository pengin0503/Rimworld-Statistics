namespace RimworldStatistics.Core;

public static class MetricObservationOrder
{
    public static MetricObservation? Latest(IEnumerable<MetricObservation> observations)
    {
        MetricObservation? best = null;
        DateTime bestUtc = default;
        foreach (var candidate in observations)
        {
            var candidateUtc = UtcTime.ToUtc(candidate.ObservedUtc);
            if (best is null
                || candidate.SaveTick > best.SaveTick
                || (candidate.SaveTick == best.SaveTick
                    && (candidateUtc > bestUtc
                        || (candidateUtc == bestUtc && string.CompareOrdinal(candidate.Key, best.Key) < 0))))
            {
                best = candidate;
                bestUtc = candidateUtc;
            }
        }
        return best;
    }
}
