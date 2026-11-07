namespace RimworldStatistics.Core;

/// <summary>Phase D2 Biotech: home map pollution and per-mechanitor bandwidth used, detached at save.</summary>
public static class BiotechStateSnapshot
{
    public const string PollutionCellsPeak = "biotech.pollution.cells_peak";
    public const string BandwidthUsedPeak = "biotech.bandwidth.used_peak";

    public static Dictionary<string, MetricEntry> Build(IEnumerable<int> homeMapPollutedCells,
        IEnumerable<int> activeFreeMechanitorBandwidth, bool partial = false)
    {
        long cells = 0;
        int? bandwidth = null;
        foreach (var n in homeMapPollutedCells)
        {
            if (n < 0)
            {
                partial = true;
                continue;
            }
            cells += n;
        }
        foreach (var n in activeFreeMechanitorBandwidth)
        {
            if (n < 0)
            {
                partial = true;
                continue;
            }
            bandwidth = bandwidth.HasValue ? Math.Max(bandwidth.Value, n) : n;
        }
        var c = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [PollutionCellsPeak] = MetricEntry.Of(MetricKeys.Revision(PollutionCellsPeak), new MetricValue
            {
                Coverage = c, IntegerValue = cells,
            }),
            [BandwidthUsedPeak] = MetricEntry.Of(MetricKeys.Revision(BandwidthUsedPeak), new MetricValue
            {
                Coverage = bandwidth.HasValue ? c : MetricCoverage.NotApplicable, IntegerValue = bandwidth,
            }),
        };
    }
}
