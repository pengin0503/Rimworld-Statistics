namespace RimworldStatistics.Core;

public sealed record ArchiveDrop(string Key, MetricEntry Entry, int ValueCount);
public sealed record ArchiveStep(MetricObservation Observation, IReadOnlyList<ArchiveDrop> Drops);

public static class CheckpointArchivePlanner
{
    public static IReadOnlyList<ArchiveStep> Plan(Profile profile)
    {
        if (!profile.MetricObservations.Any(CanDrop))
            return Array.Empty<ArchiveStep>();

        var checkpoints = profile.MetricObservations.Where(o => o.Source == MetricObservationSource.Checkpoint
            && !o.IsQuarantined && !o.Conflicted && !string.IsNullOrEmpty(o.Identity.ModCampaignId));
        var children = checkpoints.Where(o => o.ParentKey is not null)
            .ToLookup(o => (o.Identity.ModCampaignId, o.ParentKey));
        var keys = MetricRegistry.Compose(profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits)
            .GroupBy(d => d.StoredKey, StringComparer.Ordinal)
            .Where(g => g.All(d => d.Source == MetricObservationSource.Checkpoint
                && d.Selection == ColonySelection.Representative && !SnapshotSelectionPolicy.UsesLastCapture(d)))
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var result = new List<ArchiveStep>();
        foreach (var ancestor in checkpoints)
        {
            if (!CanDrop(ancestor))
                continue;
            var child = MetricObservationOrder.Latest(children[(ancestor.Identity.ModCampaignId, ancestor.Key)]
                .Where(o => o.SaveTick > ancestor.SaveTick));
            if (child is null)
                continue;

            var drops = new List<ArchiveDrop>();
            foreach (var (key, entry) in ancestor.Metrics)
                if (keys.Contains(key) && !entry.IsQuarantined && !HasExtensionData(entry)
                    && child.Metrics.TryGetValue(key, out var confirmed)
                    && !confirmed.IsQuarantined && confirmed.Revision == entry.Revision)
                    drops.Add(new ArchiveDrop(key, entry, entry.Values.Count));

            if (drops.Count > 0)
                result.Add(new ArchiveStep(ancestor, drops));
        }
        return result;
    }

    public static void Apply(ArchiveStep step)
    {
        var observation = step.Observation;
        if (!CanDrop(observation))
            return;
        foreach (var drop in step.Drops)
            if (observation.Metrics.TryGetValue(drop.Key, out var entry)
                && ReferenceEquals(entry, drop.Entry) && !entry.IsQuarantined
                && entry.Values.Count == drop.ValueCount && !HasExtensionData(entry))
            {
                observation.Metrics.Remove(drop.Key);
                observation.ArchivedMetrics = true;
            }
    }

    private static bool CanDrop(MetricObservation observation)
        => observation.Source == MetricObservationSource.Checkpoint && observation.Compressed && !observation.Conflicted && !observation.ArchivedMetrics
            && !observation.IsQuarantined && !string.IsNullOrEmpty(observation.Identity.ModCampaignId);

    private static bool HasExtensionData(MetricEntry entry)
        => entry.ExtensionData is { Count: > 0 } || entry.Values.Values.Any(v => v.ExtensionData is { Count: > 0 });
}
