namespace RimworldStatistics.Core;

public sealed record ArchiveDrop(string Key, MetricEntry Entry, int ValueCount);
public sealed record ArchiveStep(MetricObservation Observation, IReadOnlyList<ArchiveDrop> Drops)
{
    public IReadOnlyList<string> DropEvidenceUnits { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DropProviderIds { get; init; } = Array.Empty<string>();
}

public static class CheckpointArchivePlanner
{
    internal static HashSet<string> LastCaptureCheckpointKeys(Profile profile)
        => MetricRegistry.Compose(profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits)
            .GroupBy(d => d.StoredKey, StringComparer.Ordinal)
            .Where(g => g.All(d => d.Source == MetricObservationSource.Checkpoint
                && d.Selection == ColonySelection.Representative)
                && g.Any(SnapshotSelectionPolicy.UsesLastCapture))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<ArchiveStep> Plan(Profile profile)
    {
        if (!profile.MetricObservations.Any(CanDrop))
            return Array.Empty<ArchiveStep>();

        var checkpoints = profile.MetricObservations.Where(o => o.Source == MetricObservationSource.Checkpoint
            && !o.IsQuarantined && !o.Conflicted && !string.IsNullOrEmpty(o.Identity.ModCampaignId));
        var children = checkpoints.Where(o => o.ParentKey is not null)
            .ToLookup(o => (o.Identity.ModCampaignId, o.ParentKey));
        var composed = MetricRegistry.Compose(profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits);

        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var lastCaptureKeys = new HashSet<string>(StringComparer.Ordinal);
        var acquisitionsByKey = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var providersByKey = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var group in composed.GroupBy(d => d.StoredKey, StringComparer.Ordinal))
        {
            knownKeys.Add(group.Key);
            var isRepresentativeCheckpoint = true;
            var hasLastCapture = false;
            HashSet<string>? acq = null;
            HashSet<string>? prov = null;

            foreach (var d in group)
            {
                if (d.Source != MetricObservationSource.Checkpoint || d.Selection != ColonySelection.Representative)
                    isRepresentativeCheckpoint = false;
                if (SnapshotSelectionPolicy.UsesLastCapture(d))
                    hasLastCapture = true;
                if (d.Acquisition is { Length: > 0 } unit)
                    (acq ??= new HashSet<string>(StringComparer.Ordinal)).Add(unit);
                if (d.ProviderId is { Length: > 0 } providerId)
                    (prov ??= new HashSet<string>(StringComparer.Ordinal)).Add(providerId);
            }

            if (isRepresentativeCheckpoint)
            {
                keys.Add(group.Key);
                if (hasLastCapture)
                    lastCaptureKeys.Add(group.Key);
            }
            if (acq is not null)
                acquisitionsByKey[group.Key] = acq;
            if (prov is not null)
                providersByKey[group.Key] = prov;
        }

        var result = new List<ArchiveStep>();
        var validChildren = new List<MetricObservation>();
        var droppedSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ancestor in checkpoints)
        {
            if (!CanDrop(ancestor))
                continue;

            validChildren.Clear();
            foreach (var candidate in children[(ancestor.Identity.ModCampaignId, ancestor.Key)])
                if (candidate.SaveTick > ancestor.SaveTick)
                    validChildren.Add(candidate);

            var child = MetricObservationOrder.Latest(validChildren);
            if (child is null)
                continue;

            var drops = new List<ArchiveDrop>();
            droppedSet.Clear();
            foreach (var (key, entry) in ancestor.Metrics)
            {
                if (!keys.Contains(key) || entry.IsQuarantined || HasExtensionData(entry))
                    continue;

                if (lastCaptureKeys.Contains(key))
                {
                    var allConfirmed = true;
                    foreach (var validChild in validChildren)
                    {
                        if (!validChild.Metrics.TryGetValue(key, out var branchConfirmed)
                            || branchConfirmed.IsQuarantined || branchConfirmed.Revision != entry.Revision)
                        {
                            allConfirmed = false;
                            break;
                        }
                    }
                    if (!allConfirmed)
                        continue;
                }
                else
                {
                    if (!child.Metrics.TryGetValue(key, out var confirmed)
                        || confirmed.IsQuarantined || confirmed.Revision != entry.Revision)
                        continue;
                }

                drops.Add(new ArchiveDrop(key, entry, entry.Values.Count));
                droppedSet.Add(key);
            }

            if (drops.Count == 0)
                continue;

            var (dropEvidenceUnits, dropProviderIds) = PlanMetadataDrops(
                ancestor, droppedSet, knownKeys, acquisitionsByKey, providersByKey);

            result.Add(new ArchiveStep(ancestor, drops)
            {
                DropEvidenceUnits = dropEvidenceUnits,
                DropProviderIds = dropProviderIds,
            });
        }
        return result;
    }

    private static (IReadOnlyList<string> DropEvidenceUnits, IReadOnlyList<string> DropProviderIds) PlanMetadataDrops(
        MetricObservation ancestor,
        HashSet<string> droppedSet,
        HashSet<string> knownKeys,
        Dictionary<string, HashSet<string>> acquisitionsByKey,
        Dictionary<string, HashSet<string>> providersByKey)
    {
        var hasEvidence = ancestor.EvidenceByAcquisition is { Count: > 0 };
        var hasProviders = ancestor.ProviderCaptures is { Count: > 0 };
        if (!hasEvidence && !hasProviders)
            return (Array.Empty<string>(), Array.Empty<string>());

        var protectedUnits = new HashSet<string>(StringComparer.Ordinal);
        var protectedProviders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (remKey, remEntry) in ancestor.Metrics)
        {
            if (droppedSet.Contains(remKey))
                continue;
            // Any unknown or quarantined remaining entry could reference any acquisition/provider metadata.
            if (remEntry.IsQuarantined || !knownKeys.Contains(remKey))
                return (Array.Empty<string>(), Array.Empty<string>());
            if (acquisitionsByKey.TryGetValue(remKey, out var remUnits))
                protectedUnits.UnionWith(remUnits);
            if (providersByKey.TryGetValue(remKey, out var remProvs))
                protectedProviders.UnionWith(remProvs);
        }

        List<string>? dropUnits = null;
        if (hasEvidence)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var droppedKey in droppedSet)
            {
                if (!acquisitionsByKey.TryGetValue(droppedKey, out var units))
                    continue;
                foreach (var unit in units)
                    if (!protectedUnits.Contains(unit) && ancestor.EvidenceByAcquisition!.ContainsKey(unit) && seen.Add(unit))
                        (dropUnits ??= new List<string>()).Add(unit);
            }
        }

        List<string>? dropProviders = null;
        if (hasProviders)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var droppedKey in droppedSet)
            {
                if (!providersByKey.TryGetValue(droppedKey, out var providers))
                    continue;
                foreach (var providerId in providers)
                {
                    if (!protectedProviders.Contains(providerId)
                        && ancestor.ProviderCaptures!.TryGetValue(providerId, out var capture)
                        && capture.ExtensionData is not { Count: > 0 }
                        && seen.Add(providerId))
                        (dropProviders ??= new List<string>()).Add(providerId);
                }
            }
        }

        return ((IReadOnlyList<string>?)dropUnits ?? Array.Empty<string>(),
            (IReadOnlyList<string>?)dropProviders ?? Array.Empty<string>());
    }

    public static void Apply(ArchiveStep step)
    {
        var observation = step.Observation;
        if (!CanDrop(observation))
            return;
        var droppedCount = 0;
        foreach (var drop in step.Drops)
            if (observation.Metrics.TryGetValue(drop.Key, out var entry)
                && ReferenceEquals(entry, drop.Entry) && !entry.IsQuarantined
                && entry.Values.Count == drop.ValueCount && !HasExtensionData(entry))
            {
                observation.Metrics.Remove(drop.Key);
                observation.ArchivedMetrics = true;
                droppedCount++;
            }

        if (droppedCount == step.Drops.Count && droppedCount > 0)
        {
            if (observation.EvidenceByAcquisition is not null && step.DropEvidenceUnits.Count > 0)
            {
                foreach (var unit in step.DropEvidenceUnits)
                    observation.EvidenceByAcquisition.Remove(unit);
                if (observation.EvidenceByAcquisition.Count == 0)
                    observation.EvidenceByAcquisition = null;
            }
            if (observation.ProviderCaptures is not null && step.DropProviderIds.Count > 0)
            {
                foreach (var providerId in step.DropProviderIds)
                    if (observation.ProviderCaptures.TryGetValue(providerId, out var capture)
                        && capture.ExtensionData is not { Count: > 0 })
                        observation.ProviderCaptures.Remove(providerId);
                if (observation.ProviderCaptures.Count == 0)
                    observation.ProviderCaptures = null;
            }
        }
    }

    private static bool CanDrop(MetricObservation observation)
        => observation.Source == MetricObservationSource.Checkpoint && observation.Compressed && !observation.Conflicted && !observation.ArchivedMetrics
            && !observation.IsQuarantined && !string.IsNullOrEmpty(observation.Identity.ModCampaignId);

    private static bool HasExtensionData(MetricEntry entry)
        => entry.ExtensionData is { Count: > 0 } || entry.Values.Values.Any(v => v.ExtensionData is { Count: > 0 });
}
