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
        var any = false;
        var mixed = false;
        var first = MetricEvidence.UnknownLegacy;
        foreach (var v in values)
        {
            if (v == MetricEvidence.UnknownLegacy)
                return MetricEvidence.UnknownLegacy;
            if (!any) { first = v; any = true; }
            else if (v != first) mixed = true;
        }
        return !any ? MetricEvidence.UnknownLegacy : mixed ? MetricEvidence.Mixed : first;
    }

    public static MetricEvidence Combine(MetricEvidence a, MetricEvidence b)
        => a == MetricEvidence.UnknownLegacy || b == MetricEvidence.UnknownLegacy ? MetricEvidence.UnknownLegacy
            : a == b ? a : MetricEvidence.Mixed;
}
