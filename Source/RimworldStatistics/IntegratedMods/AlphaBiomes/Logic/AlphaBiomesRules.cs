using RimworldStatistics.Core;

namespace RimworldStatistics.AlphaBiomes;

internal static class AlphaBiomesRules
{
    internal const string OwnerPackageId = "sarg.alphabiomes";
    internal const string FrameworkPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal static MetricEntry? HomeBiomes(IEnumerable<(int MapId, string? BiomePackageId, string? BiomeDefName)> homes)
    {
        var seen = new HashSet<int>();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var observed = 0;
        var matchingHomes = 0;
        var partial = false;

        foreach (var (mapId, packageId, defName) in homes)
        {
            if (!seen.Add(mapId))
                continue;
            if (defName is null)
            {
                partial = true;
                continue;
            }

            observed++;
            if (!string.Equals(packageId, OwnerPackageId, StringComparison.OrdinalIgnoreCase))
                continue;

            var key = OptionalTransitionRecording.QualifiedDefKey(OwnerPackageId, defName);
            if (key is null)
            {
                partial = true;
                continue;
            }

            matchingHomes++;
            counts[key] = counts.GetValueOrDefault(key) + 1;
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
