using RimworldStatistics.Core;

namespace RimworldStatistics.AnomaliesExpected;

internal readonly record struct EntityCodexSnapshotItem(
    bool HasParentEntry,
    string? CodexPackageId,
    string? CodexDefName,
    string? ThingPackageId,
    string? ThingDefName,
    int LetterCount);

internal static class AnomaliesExpectedRules
{
    internal const string OwnerPackageId = "MrHydralisk.AnomaliesExpected";

    internal static MetricEntry? StudiedEntities(IEnumerable<EntityCodexSnapshotItem> entries)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var unlockedKeys = new HashSet<string>(StringComparer.Ordinal);
        var observedOwned = 0;

        foreach (var item in entries)
        {
            // Sub-entries propagate letters up to their parentEntityEntry, so only count root entries.
            if (item.HasParentEntry)
                continue;

            var useCodex = !string.IsNullOrWhiteSpace(item.CodexDefName);
            var pkg = useCodex ? item.CodexPackageId : item.ThingPackageId;
            var defName = useCodex ? item.CodexDefName : item.ThingDefName;

            if (!string.Equals(pkg, OwnerPackageId, StringComparison.OrdinalIgnoreCase))
                continue;

            var key = OptionalTransitionRecording.QualifiedDefKey(OwnerPackageId, defName);
            if (key is null)
                continue;

            observedOwned++;
            seenKeys.Add(key);
            if (item.LetterCount > 0)
                unlockedKeys.Add(key);
        }

        if (observedOwned == 0)
            return null;

        var entry = MetricEntry.Of(1, new MetricValue
        {
            Coverage = MetricCoverage.SnapshotOnly,
            IntegerValue = unlockedKeys.Count,
        });
        foreach (var key in unlockedKeys)
        {
            entry.Values[key] = new MetricValue
            {
                Coverage = MetricCoverage.SnapshotOnly,
                IntegerValue = 1,
            };
        }

        return entry;
    }
}
