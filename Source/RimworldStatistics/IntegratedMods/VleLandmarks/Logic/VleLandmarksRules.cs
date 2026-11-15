using RimworldStatistics.Core;

namespace RimworldStatistics.VleLandmarks;

internal static class VleLandmarksRules
{
    internal const string OwnerPackageId = "VanillaExpanded.VExplorationE";
    internal const string FrameworkPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal static MetricEntry? HomeLandmarks(IEnumerable<(int MapId, IReadOnlyList<string>? FeatureDefNames)> homes)
    {
        var seen = new HashSet<int>();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var observed = 0;
        var matchingHomes = 0;
        var partial = false;

        foreach (var (mapId, defNames) in homes)
        {
            if (!seen.Add(mapId))
                continue;
            if (defNames is null)
            {
                partial = true;
                continue;
            }

            observed++;
            var mapMatched = false;
            foreach (var defName in defNames.Distinct(StringComparer.Ordinal))
            {
                var key = OptionalTransitionRecording.QualifiedDefKey(OwnerPackageId, defName);
                if (key is null)
                {
                    partial = true;
                    continue;
                }

                mapMatched = true;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            if (mapMatched)
                matchingHomes++;
        }

        if (observed == 0)
            return null;

        var coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var entry = MetricEntry.Of(1, new MetricValue { Coverage = coverage, IntegerValue = matchingHomes });
        foreach (var (key, count) in counts)
            entry.Values[key] = new MetricValue { Coverage = coverage, IntegerValue = count };
        return entry;
    }
}
