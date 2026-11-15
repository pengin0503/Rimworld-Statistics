using RimworldStatistics.Core;

namespace RimworldStatistics.PopularModOutcomes;

internal static class PopularOutcomeRules
{
    internal const string GeologicalLandforms = "m00nl1ght.GeologicalLandforms";

    internal static bool GrowthApplied(float before, float after)
        => float.IsFinite(before) && float.IsFinite(after) && Math.Min(after, 1f) > Math.Min(before, 1f);

    internal static MetricEntry? Landforms(IEnumerable<(int Id, IReadOnlyList<string>? Forms)> homes)
    {
        var seen = new HashSet<int>();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var observed = 0;
        var partial = false;
        foreach (var (id, forms) in homes)
        {
            if (!seen.Add(id))
                continue;
            if (forms is null)
            {
                partial = true;
                continue;
            }

            observed++;
            foreach (var form in forms.Distinct(StringComparer.Ordinal))
            {
                var key = OptionalTransitionRecording.QualifiedDefKey(GeologicalLandforms, form);
                if (key is null)
                    partial = true;
                else
                    counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }
        if (observed == 0)
            return null;

        var coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var entry = MetricEntry.Of(1, new MetricValue { Coverage = coverage, IntegerValue = observed });
        foreach (var (key, count) in counts)
            entry.Values[key] = new MetricValue { Coverage = coverage, IntegerValue = count };
        return entry;
    }
}
