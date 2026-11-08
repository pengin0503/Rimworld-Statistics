namespace RimworldStatistics.Core;

/// <summary>Proof quality at the time of capture, independent of MetricCoverage.</summary>
public enum MetricEvidence
{
    UnknownLegacy,
    Experimental,
    Verified,
    Mixed,
}

public static class MetricEvidencePolicy
{
    /// <summary>Never upgrade an old observation when the acquisition unit is verified in a later release.</summary>
    public static MetricEvidence Combine(IEnumerable<MetricEvidence> values)
    {
        var seen = new HashSet<MetricEvidence>(values);
        if (seen.Count == 0 || seen.Contains(MetricEvidence.UnknownLegacy))
            return MetricEvidence.UnknownLegacy;
        if (seen.Count == 1)
            return seen.Single();
        return MetricEvidence.Mixed;
    }
}
