namespace RimworldStatistics.Core;
public static partial class ProfileMutations
{
    /// <summary>
    /// Adds a numeric observation, or merges a re-read of the same content (same source and key): file aliases are
    /// unioned, the time is the earliest copy's, and per metric a newer definition revision replaces the old one while,
    /// for the same revision, a worse value never hides a good one (numeric expansion design §2 "同じ入力の再解析").
    /// </summary>
    public static void UpsertMetricObservation(Profile p, MetricObservation obs)
    {
        if (obs.Source == MetricObservationSource.Checkpoint)
        {
            UpsertCheckpoint(p, obs);
            return;
        }
        var existing = p.MetricObservations.FirstOrDefault(o => !o.IsQuarantined && o.Source == obs.Source && o.Key == obs.Key);
        if (existing is null)
        {
            // Imported checkpoint links must survive regardless of scan order.
            // Build the alias index once; checking every checkpoint for every
            // potential save-file compaction would be quadratic on large profiles.
            var protectedFiles = obs.Source == MetricObservationSource.SaveFile
                ? p.MetricObservations.Where(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint)
                    .SelectMany(o => o.FileAliases)
                    .Select(a => (a.FileName, a.FileSize, a.LastWriteUtc)).ToHashSet()
                : null;
            if (p.MetricObservations.Any(o => Supersedes(o, obs, protectedFiles)))
                return;
            p.MetricObservations.RemoveAll(o => Supersedes(obs, o, protectedFiles));
            p.MetricObservations.Add(obs);
            if (obs.Source == MetricObservationSource.SaveSnapshot)
                CompactSaveSnapshots(p, obs);
            return;
        }
        foreach (var alias in obs.FileAliases)
            if (!existing.FileAliases.Any(a => a.Same(alias.FileName, alias.FileSize, alias.LastWriteUtc)))
                existing.FileAliases.Add(alias);
        if (obs.ObservedUtc < existing.ObservedUtc)
            existing.ObservedUtc = obs.ObservedUtc;
        existing.FormatVersion = Math.Max(existing.FormatVersion, obs.FormatVersion);
        // On an upgrade from reader v5, a re-read of identical .rws bytes
        // adds the saved MOD-set digest without losing earlier good values.
        existing.ModSetHash ??= obs.ModSetHash;
        MergeMetrics(existing, obs);
    }

    /// <summary>Per metric, a newer definition revision replaces the old one; for the same revision a worse value never hides a good one.</summary>
    private static void MergeMetrics(MetricObservation existing, MetricObservation obs)
    {
        if (obs.ProviderCaptures is { } incomingCaptures)
        {
            existing.ProviderCaptures ??= new Dictionary<string, ModProviderCapture>(Ord);
            foreach (var (providerId, capture) in incomingCaptures)
                existing.ProviderCaptures[providerId] = existing.ProviderCaptures.TryGetValue(providerId, out var previous)
                    ? ModProviderCapture.Combine(previous, capture) : capture;
        }
        // Re-reading the same snapshot must not silently overwrite its earlier
        // capture-quality evidence. A conflict is represented conservatively.
        if (obs.EvidenceByAcquisition is { } received)
        {
            existing.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(Ord);
            foreach (var (unit, evidence) in received)
                existing.EvidenceByAcquisition[unit] = existing.EvidenceByAcquisition.TryGetValue(unit, out var previous)
                    ? MetricEvidencePolicy.Combine(previous, evidence) : evidence;
        }
        if (obs.CompactedEvidenceByMetric is { } compacted)
        {
            existing.CompactedEvidenceByMetric ??= new Dictionary<string, MetricEvidence>(Ord);
            foreach (var (key, evidence) in compacted)
                existing.CompactedEvidenceByMetric[key] = existing.CompactedEvidenceByMetric.TryGetValue(key, out var previous)
                    ? MetricEvidencePolicy.Combine(previous, evidence) : evidence;
        }
        foreach (var (key, entry) in obs.Metrics)
        {
            if (entry.IsQuarantined)
                continue;
            if (!existing.Metrics.TryGetValue(key, out var old) || old.IsQuarantined || entry.Revision > old.Revision
                || (entry.Revision == old.Revision && Quality(entry) > Quality(old)))
            {
                existing.Metrics[key] = entry;
            }
            else if (existing.Source == MetricObservationSource.Checkpoint && entry.Revision == old.Revision
                && old.Values.TryGetValue(MetricEntry.Scalar, out var previous)
                && entry.Values.TryGetValue(MetricEntry.Scalar, out var incoming)
                // Re-decoding the same immutable checkpoint may reveal new breakdown
                // evidence. Do not combine children if the parent scalar changed.
                && previous.Coverage == incoming.Coverage
                && previous.IntegerValue == incoming.IntegerValue && previous.RealValue == incoming.RealValue
                && previous.Numerator == incoming.Numerator && previous.Denominator == incoming.Denominator)
            {
                foreach (var (child, value) in entry.Values)
                    if (child != MetricEntry.Scalar && !old.Values.ContainsKey(child))
                        old.Values.Add(child, value);
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, int> SaveFileRevisions = MetricRegistry.BaseCatalog
        .Where(d => d.Source == MetricObservationSource.SaveFile)
        .GroupBy(d => d.StoredKey, Ord)
        .ToDictionary(g => g.Key, g => g.First().Revision, Ord);

    /// <summary>Whether a later equivalent save can replace the older representative without discarding independent evidence.</summary>
    private static bool Supersedes(MetricObservation newer, MetricObservation older,
        IReadOnlySet<(string FileName, long FileSize, DateTime LastWriteUtc)>? checkpointFiles)
    {
        if (newer.IsQuarantined || older.IsQuarantined || ReferenceEquals(newer, older)
            || newer.Source != MetricObservationSource.SaveFile || older.Source != MetricObservationSource.SaveFile
            || newer.SaveTick <= older.SaveTick)
            return false;
        ColonyIdentity a = newer.Identity, b = older.Identity;
        if (a.FingerprintHash() is not { } hash || hash != b.FingerprintHash() || a.ModCampaignId != b.ModCampaignId
            || !a.StartingPawnIds.ToHashSet(Ord).SetEquals(b.StartingPawnIds))
            return false;

        // A pair of different or unavailable saved MOD sets cannot prove
        // equivalence. Preserve both observations instead of compacting them.
        if (newer.ModSetHash != older.ModSetHash)
            return false;
        // A checkpoint may represent a direct parent of another checkpoint.
        // Its exact .rws counter observation must not be compacted away, even
        // when the later save has the same MOD count and higher tick.
        if (checkpointFiles is not null && (newer.FileAliases.Any(a =>
                checkpointFiles.Contains((a.FileName, a.FileSize, a.LastWriteUtc)))
            || older.FileAliases.Any(a =>
                checkpointFiles.Contains((a.FileName, a.FileSize, a.LastWriteUtc)))))
            return false;
        // Historical fallback uses independently captured SaveFile rows for
        // snapshot metrics (including extrema). Preserve every such sample.
        // Also retain anything this reader cannot prove it understands.
        if (older.EvidenceByAcquisition is { Count: > 0 } || HasOpaqueData(older)
            || older.Metrics.Any(kv => !SaveFileRevisions.TryGetValue(kv.Key, out var revision)
                || kv.Value.IsQuarantined || kv.Value.Revision != revision))
            return false;
        return ModCount(newer) == ModCount(older);
    }

    /// <summary>The stored mod count and its revision; a missing or unreadable entry is its own state.</summary>
    private static (bool Readable, long? Count, int Revision) ModCount(MetricObservation o)
        => o.Metrics.TryGetValue(MetricKeys.ModsLatest, out var e)
            ? (!e.IsQuarantined, e.IsQuarantined ? null : e.Values.GetValueOrDefault(MetricEntry.Scalar)?.IntegerValue, e.Revision)
            : (true, null, 0);

    /// <summary>Good (complete, snapshot or not applicable) above partial above unknown; the worst value of the entry counts.</summary>
    private static int Quality(MetricEntry e)
        => e.Values.Count == 0 ? 2 : e.Values.Values.Min(v => v.Coverage switch
        {
            MetricCoverage.Unknown => 0,
            MetricCoverage.Partial => 1,
            _ => 2,
        });

}
