namespace RimworldStatistics.Core;

/// <summary>Phase D2 Royalty: latest eligible free-colonist psy-link levels, sampled only after a committed save.</summary>
public static class RoyaltyStateSnapshot
{
    public const string PsylinkPeak = "royalty.psylink.max";
    public static Dictionary<string, MetricEntry> Build(IEnumerable<int> freeColonistLevels, bool partial = false)
    {
        int? max = null;
        foreach (var level in freeColonistLevels)
        {
            if (level < 0)
            {
                partial = true;
                continue;
            }
            max = max.HasValue ? Math.Max(max.Value, level) : level;
        }
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [PsylinkPeak] = MetricEntry.Of(MetricKeys.Revision(PsylinkPeak), new MetricValue
            {
                Coverage = max.HasValue ? (partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly) : MetricCoverage.NotApplicable,
                IntegerValue = max,
            }),
        };
    }
}
